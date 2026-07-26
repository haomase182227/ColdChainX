using System;
using System.Collections.Generic;

namespace ColdChainX.Application.Helpers;

public sealed record TrackingPoint(
    DateTime Timestamp,
    decimal TempC,
    decimal Lat,
    decimal Lon);

public static class TrackingDownsampler
{
    public static IReadOnlyList<TrackingPoint> Downsample(
        IReadOnlyList<TrackingPoint> points,
        int maxPoints)
    {
        if (maxPoints <= 0 || points.Count <= maxPoints || maxPoints < 3)
        {
            return points;
        }

        var sampled = new List<TrackingPoint>(maxPoints) { points[0] };
        
        var every = (points.Count - 2) / (double)(maxPoints - 2);
        
        var nextPointAccumulator = 0.0;
        for (var i = 1; i < points.Count - 1; i++)
        {
            nextPointAccumulator += 1;
            if (nextPointAccumulator >= every)
            {
                sampled.Add(points[i]);
                nextPointAccumulator -= every;
            }
        }
        
        sampled.Add(points[points.Count - 1]);
        return sampled;
    }
}