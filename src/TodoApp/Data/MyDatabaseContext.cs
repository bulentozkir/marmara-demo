using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using DotNetCoreSqlDb.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace DotNetCoreSqlDb.Data
{
    public class MyDatabaseContext : IdentityDbContext<IdentityUser>
    {
        public MyDatabaseContext (DbContextOptions<MyDatabaseContext> options)
            : base(options)
        {
        }

        public DbSet<DotNetCoreSqlDb.Models.Todo> Todo { get; set; } = default!;
        public DbSet<SearchOperation> SearchOperations { get; set; } = default!;

        public void QueueSearchUpdate(Todo todo) =>
            SearchOperations.Add(new SearchOperation { DocumentId = todo.SearchDocumentId });

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<Todo>().HasIndex(todo => todo.SearchDocumentId).IsUnique();
            builder.Entity<Todo>()
                .HasOne<IdentityUser>()
                .WithMany()
                .HasForeignKey(todo => todo.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
