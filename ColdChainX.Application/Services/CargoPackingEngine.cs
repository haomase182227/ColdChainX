using System;
using System.Collections.Generic;
using System.Linq;

namespace ColdChainX.Application.Services
{
    public class LpnDims
    {
        public Guid LpnId { get; set; }
        public decimal Length { get; set; }
        public decimal Width { get; set; }
        public decimal Height { get; set; }
        public int RouteStopSequence { get; set; }
        public decimal WeightKg { get; set; }
        public decimal RequiredTemperature { get; set; }
    }

    public class ContainerDims
    {
        public decimal Length { get; set; }
        public decimal Width { get; set; }
        public decimal Height { get; set; }
    }

    public class PlacedItem
    {
        public Guid LpnId { get; set; }
        public decimal X { get; set; }
        public decimal Y { get; set; }
        public decimal Z { get; set; }
        public decimal W { get; set; } // width (X)
        public decimal H { get; set; } // height (Y)
        public decimal D { get; set; } // depth (Z)
    }

    public class PackingResult
    {
        public List<PlacedItem> PlacedItems { get; set; } = new();
        public List<Guid> UnplacedLpnIds { get; set; } = new();
        public decimal Utilisation { get; set; }
    }

    public class CargoPackingEngine
    {
        public PackingResult Pack(ContainerDims container, List<LpnDims> itemsToPack)
        {
            var result = new PackingResult();
            
            if (container.Length <= 0 || container.Width <= 0 || container.Height <= 0)
            {
                result.UnplacedLpnIds = itemsToPack.Select(i => i.LpnId).ToList();
                return result;
            }

            // 1. Airflow Circulation: Reduce usable dimensions
            decimal usableLength = container.Length - 20m; // Front 10cm + Rear 10cm
            decimal usableHeight = container.Height - 20m; // Top 20cm

            if (usableLength <= 0 || usableHeight <= 0)
            {
                result.UnplacedLpnIds = itemsToPack.Select(i => i.LpnId).ToList();
                return result;
            }

            // 2. Sorting Rules (LIFO, Temperature, Weight, Volume)
            var sortedItems = itemsToPack
                .OrderByDescending(i => i.RouteStopSequence)
                .ThenBy(i => i.RequiredTemperature) // Colder items first (front)
                .ThenByDescending(i => i.WeightKg) // Heavier items first (bottom)
                .ThenByDescending(i => i.Length * i.Width * i.Height)
                .ToList();

            var placedItems = new List<PlacedItem>();
            var unplacedIds = new List<Guid>();

            // 3. Keep-out Zone: Evaporator (Lốc lạnh) Obstacle
            // Calculate physical dimensions
            decimal evapH_physical = Math.Min(container.Height * 0.3m, 60m);
            decimal evapD_physical = 30m;
            
            // Map to packing coordinate system (which already reduced top 20cm and front 10cm)
            decimal packingEvapH = evapH_physical - 20m;
            decimal packingEvapD = evapD_physical - 10m;

            if (packingEvapH > 0 && packingEvapD > 0)
            {
                placedItems.Add(new PlacedItem
                {
                    LpnId = Guid.Empty, // Dummy ID for obstacle
                    X = 0, // Block full width
                    Y = usableHeight - packingEvapH,
                    Z = 0,
                    W = container.Width,
                    H = packingEvapH,
                    D = packingEvapD
                });
            }

            foreach (var item in sortedItems)
            {
                var orientations = GetOrientations(item);
                bool placed = false;

                // Candidate points: start with origin
                var candidatePoints = new List<(decimal x, decimal y, decimal z)> { (0, 0, 0) };

                // Add corners of already placed items as candidates
                foreach (var pi in placedItems)
                {
                    candidatePoints.Add((pi.X + pi.W, pi.Y, pi.Z));
                    candidatePoints.Add((pi.X, pi.Y + pi.H, pi.Z));
                    candidatePoints.Add((pi.X, pi.Y, pi.Z + pi.D));
                }

                // Sort candidates to pack tightly: bottom-left-back first (z, y, x)
                candidatePoints = candidatePoints
                    .OrderBy(p => p.z)
                    .ThenBy(p => p.y)
                    .ThenBy(p => p.x)
                    .ToList();

                foreach (var point in candidatePoints)
                {
                    foreach (var (w, h, d) in orientations)
                    {
                        // Check container boundaries (using reduced dimensions for Airflow)
                        if (point.x + w > container.Width ||
                            point.y + h > usableHeight ||
                            point.z + d > usableLength)
                        {
                            continue;
                        }

                        // Check collisions
                        bool collision = false;
                        foreach (var pi in placedItems)
                        {
                            if (Overlap(point.x, point.y, point.z, w, h, d,
                                        pi.X, pi.Y, pi.Z, pi.W, pi.H, pi.D))
                            {
                                collision = true;
                                break;
                            }
                        }

                        if (!collision)
                        {
                            placedItems.Add(new PlacedItem
                            {
                                LpnId = item.LpnId,
                                X = point.x,
                                Y = point.y,
                                Z = point.z,
                                W = w,
                                H = h,
                                D = d
                            });
                            placed = true;
                            break; // Stop checking orientations
                        }
                    }

                    if (placed) break; // Stop checking points
                }

                if (!placed)
                {
                    unplacedIds.Add(item.LpnId);
                }
            }

            // Remove dummy obstacles (like Evaporator)
            placedItems.RemoveAll(pi => pi.LpnId == Guid.Empty);

            // Center the cargo horizontally (along X-axis) to distribute weight evenly
            // AND offset along Z-axis for Front Bulkhead clearance (10cm)
            if (placedItems.Any())
            {
                decimal maxPlacedX = placedItems.Max(pi => pi.X + pi.W);
                decimal unusedX = container.Width - maxPlacedX;
                decimal offsetX = unusedX > 0 ? unusedX / 2m : 0;
                
                foreach (var pi in placedItems)
                {
                    if (offsetX > 0) pi.X += offsetX;
                    pi.Z += 10m; // Front Bulkhead clearance
                }
            }

            result.PlacedItems = placedItems;
            result.UnplacedLpnIds = unplacedIds;

            // Calculate utilization
            decimal totalContainerVolume = container.Length * container.Width * container.Height;
            decimal usedVolume = placedItems.Sum(pi => pi.W * pi.H * pi.D);
            result.Utilisation = totalContainerVolume > 0 ? (usedVolume / totalContainerVolume) * 100 : 0;

            return result;
        }

        private List<(decimal w, decimal h, decimal d)> GetOrientations(LpnDims item)
        {
            var l = item.Length;
            var w = item.Width;
            var h = item.Height;

            // 6 possible orientations in 3D space
            // Assuming W is X-axis (width), H is Y-axis (height), D is Z-axis (length/depth)
            var list = new List<(decimal w, decimal h, decimal d)>
            {
                (w, h, l),
                (l, h, w),
                (w, l, h),
                (h, l, w),
                (l, w, h),
                (h, w, l)
            };

            // Remove duplicates (e.g. if l == w)
            return list.Distinct().ToList();
        }

        private bool Overlap(decimal x1, decimal y1, decimal z1, decimal w1, decimal h1, decimal d1,
                             decimal x2, decimal y2, decimal z2, decimal w2, decimal h2, decimal d2)
        {
            return x1 < x2 + w2 && x1 + w1 > x2 &&
                   y1 < y2 + h2 && y1 + h1 > y2 &&
                   z1 < z2 + d2 && z1 + d1 > z2;
        }
    }
}
