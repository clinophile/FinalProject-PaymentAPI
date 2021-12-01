using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using FinalProject.Configuration;
using FinalProject.Data;
using FinalProject.Models;
using FinalProject.Models.DTOs.Requests;
using FinalProject.Models.DTOs.Responses;

namespace TodoApp.Controllers{

    [Route("api/[controller]")]
    [ApiController]
    public class AuthManagementController: ControllerBase{
        private readonly UserManager<IdentityUser> _userManager;
        private readonly JwtConfig _jwtConfig;
        private readonly TokenValidationParameters _tokenValidationParams;
        private readonly ApiDbContext _apiDbContext;


        public AuthManagementController(UserManager<IdentityUser> userManager, 
        IOptionsMonitor<JwtConfig> optionsMonitor, TokenValidationParameters tokenValidationParams,
        ApiDbContext apiDbContext)
        {
            _tokenValidationParams = tokenValidationParams ;
            _apiDbContext = apiDbContext ;
            _userManager = userManager;
            _jwtConfig = optionsMonitor.CurrentValue;
        }

        [HttpPost]
        [Route("Register")]
        public async Task<IActionResult> Register([FromBody] UserRegistrationDto user){
            if(ModelState.IsValid){
                var existingUser = await _userManager.FindByEmailAsync(user.email);

                if(existingUser != null){
                    return BadRequest(new RegistrationResponse(){
                        Errors = new List<string>(){
                            "Email already in use"
                        },
                        success = false
                    });
                }

                var newUser = new IdentityUser(){ Email = user.email, UserName = user.username};
                var isCreated = await _userManager.CreateAsync(newUser, user.password);

                if(isCreated.Succeeded){
                    var jwtToken = GenerateJwtToken(newUser);

                    return Ok(jwtToken);

                } else {
                    return BadRequest(new RegistrationResponse(){
                        Errors = isCreated.Errors.Select(x => x.Description).ToList(),
                        success = false
                    });
                }
            }

            return BadRequest(new RegistrationResponse(){
                    Errors = new List<string>(){
                        "Invalid Payload"
                    },
                    success = false
                });
        }

        private async Task<AuthResult> GenerateJwtToken(IdentityUser user){
            var jwtTokenHandler = new JwtSecurityTokenHandler();

            var key = Encoding.ASCII.GetBytes(_jwtConfig.Secret);

            var tokenDescriptor = new SecurityTokenDescriptor{
                Subject = new ClaimsIdentity(new []{
                    new Claim("Id", user.Id),
                    new Claim(JwtRegisteredClaimNames.Email, user.Email),
                    new Claim(JwtRegisteredClaimNames.Sub, user.Email),
                    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
                }),
                Expires = DateTime.UtcNow.AddHours(1),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = jwtTokenHandler.CreateToken(tokenDescriptor);
            var jwtToken = jwtTokenHandler.WriteToken(token);

            var refreshToken = new RefreshToken() {
                JwtId = token.Id,
                IsUsed = false,
                IsRevorked = false,
                UserId = user.Id,
                AddedDate = DateTime.UtcNow,
                ExpireDate = DateTime.UtcNow.AddMonths(6),
                Token = RandomString(35) + Guid.NewGuid()

            };

            await _apiDbContext.RefreshToken.AddAsync(refreshToken);
            await _apiDbContext.SaveChangesAsync();

            return new AuthResult() {
                Token = jwtToken,
                success = true,
                RefreshToken = refreshToken.Token
            };
        }

        private string RandomString(int length) {
            var random = new Random();
            var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

            return new string(Enumerable.Repeat(chars, length).Select(x => x[random.Next(x.Length)]).ToArray());
        }


        [HttpPost]
        [Route("Login")]
        public async Task<IActionResult> Login([FromBody] UserLoginRequest user){
            if(ModelState.IsValid){
                var existingUser = await _userManager.FindByEmailAsync(user.Email);

                if(existingUser == null){
                    return BadRequest(new RegistrationResponse(){
                        Errors = new List<string>(){
                            "Invalid login request"
                        },
                        success = false
                    });
                }

                var isCorrect = await _userManager.CheckPasswordAsync(existingUser, user.Password);

                if(!isCorrect){
                    return BadRequest(new RegistrationResponse(){
                        Errors = new List<string>(){
                            "Invalid login request"
                        },
                        success = false
                    });
                }

                var jwtToken = GenerateJwtToken(existingUser);

                return Ok(jwtToken);
            }

            return BadRequest(new RegistrationResponse(){
                Errors = new List<string>(){
                    "Invalid payload"
                },
                success = false
            });
        }

        [HttpPost]
        [Route("RefreshToken")]
        public async Task<IActionResult> RefreshToken([FromBody] TokenRequest tokenRequest) {
            if (ModelState.IsValid) {
                var result = await VerifyAndGenerateToken(tokenRequest);

                if (result == null) {
                    return BadRequest(new RegistrationResponse() {
                        Errors = new List<string>() {
                            "Invalid tokens"
                        },
                        success = false
                    });
                }
                
                return Ok(result);
            }

            return BadRequest(new RegistrationResponse(){
                Errors = new List<string>(){
                    "Invalid payload"
                },
                success = false
            });
        }

        private async Task<AuthResult> VerifyAndGenerateToken(TokenRequest tokenRequest) {
            var jwtTokenHandler = new JwtSecurityTokenHandler();

            try {
                var tokenInVerification = jwtTokenHandler.ValidateToken(tokenRequest.Token, _tokenValidationParams, out var validatedToken);

                if (validatedToken is JwtSecurityToken jwtSecurityToken) {
                    var result = jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase);

                    if(result == false) {
                        return null;
                    }
                }

                var utcExpireDate = long.Parse(tokenInVerification.Claims.FirstOrDefault(x => x.Type == JwtRegisteredClaimNames.Exp).Value);

                var expireDate = UnixTimeStampToDateTime(utcExpireDate);

                if (expireDate > DateTime.UtcNow) {
                    return new AuthResult() {
                        success = false,
                        Errors = new List<string>() {
                            "Token has not yet expired"
                        }
                    };
                }

                var storedToken = await _apiDbContext.RefreshToken.FirstOrDefaultAsync(x => x.Token == tokenRequest.RefreshToken);
                if (storedToken == null) {
                    return new AuthResult() {
                        success = false,
                        Errors = new List<string>() {
                            "Token does not exist"
                        }
                    };
                }

                if(storedToken.IsUsed) {
                    return new AuthResult() {
                        success = false,
                        Errors = new List<string>() {
                            "Token has been used"
                        }
                    };
                }

                if(storedToken.IsRevorked) {
                    return new AuthResult() {
                        success = false,
                        Errors = new List<string>() {
                            "Token has been revoked"
                        }
                    };
                }

                var jti = tokenInVerification.Claims.FirstOrDefault(x => x.Type == JwtRegisteredClaimNames.Jti).Value;
                if(storedToken.JwtId != jti) {
                    return new AuthResult() {
                        success = false,
                        Errors = new List<string>() {
                            "Token doesn't match"
                        }
                    };
                }

                storedToken.IsUsed = true;
                _apiDbContext.RefreshToken.Update(storedToken);
                await _apiDbContext.SaveChangesAsync();

                //generate new token
                var dbUser = await _userManager.FindByIdAsync(storedToken.UserId);
                return await GenerateJwtToken(dbUser);
            }
            catch(Exception ex) {
                if(ex.Message.Contains("Lifetime validation failed. The token is expired.")) {
                    return new AuthResult() {
                        success = false,
                        Errors = new List<string>() {
                            "Token has expired, please re-login"
                        }
                    };
                }
                else {
                     return new AuthResult() {
                        success = false,
                        Errors = new List<string>() {
                            "Something went wrong"
                        }
                    };

                }
            }
        }

        private DateTime UnixTimeStampToDateTime(long unixTimeStamp) {
            var dateTimeVal = new DateTime(1970, 1,1,0,0,0,0, DateTimeKind.Utc);
            dateTimeVal = dateTimeVal.AddSeconds(unixTimeStamp).ToUniversalTime();
            
            return dateTimeVal;
        }
    }
}