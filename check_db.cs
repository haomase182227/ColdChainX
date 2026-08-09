using System;
using System.Linq;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Core.Entities;

var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
optionsBuilder.UseNpgsql("Host=localhost;Database=coldchain;Username=postgres;Password=1");
using var db = new ApplicationDbContext(optionsBuilder.Options);
var vehicle = db.Vehicles.FirstOrDefault();
Console.WriteLine(vehicle?.CurrentLocation);
var user = db.Drivers.FirstOrDefault(d => d.WarehouseId != null);
Console.WriteLine(user?.WarehouseId);
