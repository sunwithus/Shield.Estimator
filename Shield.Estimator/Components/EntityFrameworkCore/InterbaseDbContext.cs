//InterbaseDbContext.cs

using Microsoft.EntityFrameworkCore;
using Shield.Estimator.Shared.Components.EntityFrameworkCore;
using Shield.Estimator.Shared.Components.EntityFrameworkCore.Sprutora;

namespace Shield.Estimator.Shared.Components.EntityFrameworkCore
{
    // Определение контекста базы данных для работы с Entity Framework Core
    public class InterbaseDbContext : BaseDbContext
    {
        public InterbaseDbContext(DbContextOptions<InterbaseDbContext> options) : base(options)
        {
        }
        public override DbSet<SprSpeechTable> SprSpeechTables { get; set; }
        public override DbSet<SprSpData1Table> SprSpData1Tables { get; set; }
        public override DbSet<SprSpCommentTable> SprSpCommentTables { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {

        }
    }

}

