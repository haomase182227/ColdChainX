using Microsoft.EntityFrameworkCore;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;

namespace ColdChainX.Infrastructure.Persistence
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(b =>
            {
                b.HasKey(u => u.Id);
                b.Property(u => u.Email).IsRequired().HasMaxLength(256);
                b.Property(u => u.FullName).IsRequired().HasMaxLength(200);
                b.Property(u => u.PasswordHash).IsRequired();
                b.Property(u => u.PhoneNumber).HasMaxLength(50);
                b.Property(u => u.Role).IsRequired();
                b.Property(u => u.Status).IsRequired().HasDefaultValue(UserStatus.Active);
                b.Property(u => u.CreatedAt).IsRequired();
                b.Property(u => u.UpdatedAt);
                b.Property(u => u.RefreshToken).HasMaxLength(500);
            });
        }
    }
}
