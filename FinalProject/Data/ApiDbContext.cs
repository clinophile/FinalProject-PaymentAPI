using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FinalProject.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FinalProject.Data{
    public class ApiDbContext: IdentityDbContext{
        public virtual DbSet<PaymentDetails> Items { get; set; }
        public virtual DbSet<RefreshToken> RefreshToken { get; set; }
       
        public ApiDbContext(DbContextOptions<ApiDbContext> options): base(options){ 
        }

        
    }
}