using Microsoft.EntityFrameworkCore;

namespace ResourceHub.Api.Data;

// Tables (spec §5) are added in a later phase.
public class HubDbContext(DbContextOptions<HubDbContext> options) : DbContext(options)
{
}
