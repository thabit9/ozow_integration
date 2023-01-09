using Ozow_Integration.Models;
using Ozow_Integration.Ozow;
using Microsoft.EntityFrameworkCore;

namespace Ozow_Integration.DataAccess
{
    public class OzowContext : DbContext
    {
        public OzowContext()
        {            
        }
        public OzowContext(DbContextOptions<OzowContext> options) 
            : base(options)
        {
        } 
        public virtual DbSet<Transaction> Transactions { get; set; }
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        { 
            //code here...             
        }
    }
}