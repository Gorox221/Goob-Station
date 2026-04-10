using System.Numerics;
using System.Linq;
using Content.Server.Station.Components;
using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Content.Shared._Shiptest.SpaceBiomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using TransformSystem = Robust.Server.GameObjects.TransformSystem;

namespace Content.Server._Shiptest.SpaceBiomes;

/// <summary>
/// Shape types for biome boundary generation.
/// </summary>
internal enum BiomeShape
{
    Lake,       // Circular blob with noise (default)
    River,      // Long winding channel
    Crescent,   // Moon-shaped arc
    Squid,      // Body with trailing tentacles
    Cloud,      // Fluffy blob with extensions
    Fjord,      // Narrow winding inlet
    Archipelago,// Cluster of small blobs
    Spiral      // Swirling vortex
}

/// <summary>
/// A candidate position for a biome source during placement.
/// </summary>
internal readonly struct BiomeSlot
{
    public readonly Vector2 Position;
    public readonly float SlotRadius;

    public BiomeSlot(Vector2 position, float slotRadius)
    {
        Position = position;
        SlotRadius = slotRadius;
    }
}

/// <summary>
/// A spawned biome source with its generated boundary shape.
/// </summary>
internal sealed class SpawnedBiomeSource
{
    public Vector2 Position;
    public string BiomeId = "";
    public int Priority;
    public List<BiomeBoundaryPoint> BoundaryPoints = new();
    public float AverageRadius;
}

/// <summary>
/// Automatically spawns space biome sources around stations at round start.
/// Uses a two-phase approach:
/// 1. Generate non-overlapping slot positions (circle packing)
/// 2. Assign biome types and generate organic boundary shapes
/// </summary>
public sealed class SpaceBiomeSpawnerSystem : EntitySystem
{
    [Dependency] private readonly StationSystem _stationSystem = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _protoMan = default!;

    private ISawmill _sawmill = default!;

    /// <summary>
    /// How many orbital bands to create around the station.
    /// </summary>
    private const int OrbitalBands = 24;

    /// <summary>
    /// Minimum distance from the grid edge to the first band.
    /// </summary>
    private const float MinOrbitPadding = 200f;

    /// <summary>
    /// Maximum extent of the outermost band (from grid center).
    /// Biomes will spawn up to this distance from the station.
    /// </summary>
    private const float MaxOrbitExtent = 24000f;

    /// <summary>
    /// Total number of biome sources to spawn per station.
    /// Calculated to maintain ~400m spacing across the full 24km range.
    /// </summary>
    private const int TotalSources = 800;

    /// <summary>
    /// Number of boundary points per biome shape.
    /// </summary>
    private const int BoundaryResolution = 24;

    /// <summary>
    /// Noise magnitude for boundary shape deformation (fraction of radius).
    /// </summary>
    private const float BoundaryNoiseMagnitude = 0.4f;

    /// <summary>
    /// Range of biome radii: [MinRadiusFactor, MaxRadiusFactor] of the slot radius.
    /// MaxRadiusFactor MUST be ≤ 1.0 to guarantee no overlap between biomes.
    /// </summary>
    private const float MinRadiusFactor = 0.3f;
    private const float MaxRadiusFactor = 1.0f;

    /// <summary>
    /// Fixed radius for each biome source (in meters).
    /// Keeps biome sizes consistent regardless of station size.
    /// </summary>
    private const int FixedBiomeRadius = 400;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = IoCManager.Resolve<ILogManager>().GetSawmill("biome_spawner");
        SubscribeLocalEvent<StationPostInitEvent>(OnStationPostInit);
    }

    private void OnStationPostInit(ref StationPostInitEvent ev)
    {
        var gridUid = _stationSystem.GetLargestGrid((ev.Station.Owner, ev.Station.Comp));
        if (!gridUid.HasValue)
            return;

        var gridXform = Transform(gridUid.Value);
        var gridCenter = _transform.GetWorldPosition(gridXform);
        var gridRadius = CalculateGridRadius(gridUid.Value);
        var mapId = gridXform.MapID;

        SpawnBiomeOrganic(gridCenter, gridRadius, mapId);
    }

    private float CalculateGridRadius(EntityUid gridUid)
    {
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return 200f;

        var halfSize = grid.LocalAABB.Size / 2f;
        return halfSize.Length();
    }

    private void SpawnBiomeOrganic(Vector2 center, float gridRadius, MapId mapId)
    {
        // Collect available biome prototypes
        var availableBiomes = new List<string>();
        foreach (var proto in _protoMan.EnumeratePrototypes<SpaceBiomePrototype>())
        {
            if (proto.ID == "DefaultSpace")
                continue;
            availableBiomes.Add(proto.ID);
        }

        if (availableBiomes.Count == 0)
            return;

        _random.Shuffle(availableBiomes);

        // Phase 1: Generate non-overlapping slot positions
        var slots = GenerateNonOverlappingSlots(center, gridRadius, TotalSources);

        if (slots.Count == 0)
            return;

        // Phase 2: Assign biomes and generate shapes
        var placedSources = new List<SpawnedBiomeSource>();
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];

            // Assign biome type (cycle through shuffled list)
            var biomeId = availableBiomes[i % availableBiomes.Count];

            // Priority: inner slots higher (based on distance from center)
            var distFromCenter = Vector2.Distance(slot.Position, center);
            var t = Math.Clamp((distFromCenter - gridRadius - MinOrbitPadding) / (MaxOrbitExtent), 0f, 1f);
            var priority = (int)(100 * (1f - t) + _random.Next(-5, 5));

            // Random radius for this biome (within slot limits)
            var biomeRadius = slot.SlotRadius * _random.NextFloat(MinRadiusFactor, MaxRadiusFactor);

            // Generate organic boundary shape
            var boundaryPoints = GenerateOrganicBoundary(biomeRadius, BoundaryResolution, BoundaryNoiseMagnitude);
            var avgRadius = boundaryPoints.Average(p => p.Radius);

            placedSources.Add(new SpawnedBiomeSource
            {
                Position = slot.Position,
                BiomeId = biomeId,
                Priority = Math.Max(0, priority),
                BoundaryPoints = boundaryPoints,
                AverageRadius = avgRadius
            });
        }

        // Phase 3: Spawn entities
        var spawnedCount = 0;
        foreach (var source in placedSources)
        {
            SpawnBiomeEntity(source, mapId);
            spawnedCount++;
        }
        _sawmill.Info($"Spawned {spawnedCount} biome sources around station");
    }

    /// <summary>
    /// Phase 1: Generate non-overlapping slot positions using orbital band placement
    /// with strict distance enforcement.
    /// Each slot uses a fixed radius for consistent biome sizes.
    /// </summary>
    private List<BiomeSlot> GenerateNonOverlappingSlots(Vector2 center, float gridRadius, int count)
    {
        var slots = new List<BiomeSlot>();
        var minDist = gridRadius + MinOrbitPadding;
        var maxDist = gridRadius + MaxOrbitExtent;

        // Use fixed biome radius — keeps sizes consistent at all distances
        var slotRadius = FixedBiomeRadius;

        // Place slots on orbital bands
        var sourcesPerBand = count / OrbitalBands;
        var extraSources = count - sourcesPerBand * OrbitalBands;
        var ringSpan = (maxDist - minDist) / OrbitalBands;

        for (var band = 0; band < OrbitalBands; band++)
        {
            var bandCount = sourcesPerBand + (band < extraSources ? 1 : 0);
            if (bandCount == 0)
                continue;

            var bandDistance = minDist + ringSpan * (band + 0.5f);

            // Angular separation — ensure slots don't overlap within the band
            // Arc length at this distance = 2π × bandDistance / bandCount
            // We want arc length >= 2 × slotRadius for no overlap
            var angleStep = MathF.Tau / bandCount;

            // Add angular jitter (up to 20% of the step)
            var jitterRange = angleStep * 0.2f;

            for (var i = 0; i < bandCount; i++)
            {
                var baseAngle = i * angleStep;
                var jitter = _random.NextFloat(-jitterRange, jitterRange);
                var angle = baseAngle + jitter;

                // Radial jitter (small, within 15% of slot radius)
                var radialJitter = _random.NextFloat(-slotRadius * 0.15f, slotRadius * 0.15f);
                var distance = bandDistance + radialJitter;
                distance = Math.Clamp(distance, minDist, maxDist);

                var position = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;

                // Final overlap check against all existing slots
                var overlaps = false;
                foreach (var existing in slots)
                {
                    var dist = Vector2.Distance(position, existing.Position);
                    var minRequired = slotRadius + existing.SlotRadius;
                    if (dist < minRequired)
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (!overlaps)
                {
                    slots.Add(new BiomeSlot(position, slotRadius));
                }
            }
        }

        _sawmill.Info($"Generated {slots.Count}/{count} biome slots with radius {slotRadius}m from {minDist:F0}m to {maxDist:F0}m");
        return slots;
    }

    /// <summary>
    /// Generates a biome boundary shape. Picks a random shape type and generates
    /// boundary points accordingly.
    /// </summary>
    private List<BiomeBoundaryPoint> GenerateOrganicBoundary(
        float baseRadius,
        int resolution,
        float noiseMagnitude)
    {
        var shape = (BiomeShape)_random.Next(Enum.GetValues(typeof(BiomeShape)).Length);

        return shape switch
        {
            BiomeShape.Lake       => GenerateLake(baseRadius, resolution, noiseMagnitude),
            BiomeShape.River      => GenerateRiver(baseRadius, resolution),
            BiomeShape.Crescent   => GenerateCrescent(baseRadius, resolution),
            BiomeShape.Squid      => GenerateSquid(baseRadius, resolution),
            BiomeShape.Cloud      => GenerateCloud(baseRadius, resolution, noiseMagnitude),
            BiomeShape.Fjord      => GenerateFjord(baseRadius, resolution),
            BiomeShape.Archipelago=> GenerateArchipelago(baseRadius, resolution),
            BiomeShape.Spiral     => GenerateSpiral(baseRadius, resolution),
            _                     => GenerateLake(baseRadius, resolution, noiseMagnitude),
        };
    }

    /// <summary>
    /// Lake: circular blob with smooth noise (classic shape).
    /// </summary>
    private List<BiomeBoundaryPoint> GenerateLake(float baseRadius, int resolution, float noiseMagnitude)
    {
        var points = new List<BiomeBoundaryPoint>(resolution);
        var angleStep = MathF.Tau / resolution;

        var noiseValues = new float[resolution];
        for (var i = 0; i < resolution; i++)
            noiseValues[i] = 1f + _random.NextFloat(-noiseMagnitude, noiseMagnitude);

        var smoothed = new float[resolution];
        for (var i = 0; i < resolution; i++)
        {
            var prev = noiseValues[(i - 1 + resolution) % resolution];
            var curr = noiseValues[i];
            var next = noiseValues[(i + 1) % resolution];
            smoothed[i] = prev * 0.25f + curr * 0.5f + next * 0.25f;
        }

        for (var i = 0; i < resolution; i++)
        {
            var angle = i * angleStep;
            var radius = baseRadius * smoothed[i];
            radius = MathF.Max(baseRadius * 0.3f, radius);
            points.Add(new BiomeBoundaryPoint(angle, radius));
        }

        return points;
    }

    /// <summary>
    /// River: elongated winding channel.
    /// </summary>
    private List<BiomeBoundaryPoint> GenerateRiver(float baseRadius, int resolution)
    {
        var points = new List<BiomeBoundaryPoint>(resolution);
        var angleStep = MathF.Tau / resolution;
        var riverAngle = _random.NextFloat(0, MathF.Tau);
        var riverWidth = 0.25f + _random.NextFloat(-0.05f, 0.05f);

        for (var i = 0; i < resolution; i++)
        {
            var angle = i * angleStep;
            var relAngle = angle - riverAngle;
            while (relAngle < 0) relAngle += MathF.Tau;
            while (relAngle >= MathF.Tau) relAngle -= MathF.Tau;

            // River is wide along the axis, narrow perpendicular to it
            var elongation = 2.5f + _random.NextFloat(-0.3f, 0.3f);
            var widthFactor = MathF.Cos(relAngle);
            var radius = baseRadius * riverWidth / (riverWidth * riverWidth + (1 - riverWidth) * widthFactor * widthFactor + 0.01f);
            radius *= elongation * MathF.Abs(MathF.Cos(relAngle)) + 0.3f;
            radius = MathF.Max(baseRadius * 0.1f, MathF.Min(radius, baseRadius * elongation));

            // Add small meander noise
            var meander = _random.NextFloat(-0.1f, 0.1f) * baseRadius;
            radius += meander;

            points.Add(new BiomeBoundaryPoint(angle, radius));
        }

        return points;
    }

    /// <summary>
    /// Crescent: moon-shaped arc with rounded edges.
    /// </summary>
    private List<BiomeBoundaryPoint> GenerateCrescent(float baseRadius, int resolution)
    {
        var points = new List<BiomeBoundaryPoint>(resolution);
        var angleStep = MathF.Tau / resolution;
        var crescentOffset = _random.NextFloat(0.35f, 0.55f);
        var crescentAngle = _random.NextFloat(0, MathF.Tau);
        var arcWidth = _random.NextFloat(0.15f, 0.3f);

        for (var i = 0; i < resolution; i++)
        {
            var angle = i * angleStep;
            var relAngle = angle - crescentAngle;
            while (relAngle < -MathF.PI) relAngle += MathF.Tau;
            while (relAngle >= MathF.PI) relAngle -= MathF.Tau;

            // Crescent: outer circle minus inner circle offset
            var outerR = baseRadius;
            var innerR = baseRadius * crescentOffset;
            var innerDist = MathF.Sqrt(innerR * innerR + baseRadius * baseRadius - 2 * innerR * baseRadius * MathF.Cos(relAngle - arcWidth));

            var radius = MathF.Max(0.05f, outerR - innerDist + baseRadius * 0.15f);
            var noise = 1f + _random.NextFloat(-0.1f, 0.1f);
            radius *= noise;

            points.Add(new BiomeBoundaryPoint(angle, radius));
        }

        return points;
    }

    /// <summary>
    /// Squid: rounded body with trailing tentacle lobes.
    /// </summary>
    private List<BiomeBoundaryPoint> GenerateSquid(float baseRadius, int resolution)
    {
        var points = new List<BiomeBoundaryPoint>(resolution);
        var angleStep = MathF.Tau / resolution;
        var squidAngle = _random.NextFloat(0, MathF.Tau);
        var tentacleCount = _random.Next(3, 5);
        var tentacleAngles = Enumerable.Range(0, tentacleCount)
            .Select(_ => _random.NextFloat(0, MathF.Tau))
            .OrderBy(a => a)
            .ToList();

        for (var i = 0; i < resolution; i++)
        {
            var angle = i * angleStep;
            var relAngle = angle - squidAngle;
            while (relAngle < 0) relAngle += MathF.Tau;
            while (relAngle >= MathF.Tau) relAngle -= MathF.Tau;

            // Body: wider at top (away from tentacles), narrower at bottom
            var bodyFactor = 0.6f + 0.4f * MathF.Cos(relAngle);
            var radius = baseRadius * bodyFactor;

            // Tentacles: bumps at the back
            foreach (var tAngle in tentacleAngles)
            {
                var tRel = angle - tAngle;
                while (tRel < -MathF.PI) tRel += MathF.Tau;
                while (tRel >= MathF.PI) tRel -= MathF.Tau;

                var tentacleFactor = MathF.Exp(-tRel * tRel * 4f) * 0.6f;
                radius += baseRadius * tentacleFactor;
            }

            var noise = 1f + _random.NextFloat(-0.08f, 0.08f);
            radius = MathF.Max(baseRadius * 0.08f, radius * noise);

            points.Add(new BiomeBoundaryPoint(angle, radius));
        }

        return points;
    }

    /// <summary>
    /// Cloud: fluffy blob with random outward extensions.
    /// </summary>
    private List<BiomeBoundaryPoint> GenerateCloud(float baseRadius, int resolution, float noiseMagnitude)
    {
        var points = new List<BiomeBoundaryPoint>(resolution);
        var angleStep = MathF.Tau / resolution;

        // Start with lake noise
        var noiseValues = new float[resolution];
        for (var i = 0; i < resolution; i++)
            noiseValues[i] = 1f + _random.NextFloat(-noiseMagnitude * 0.5f, noiseMagnitude * 0.5f);

        var smoothed = new float[resolution];
        for (var i = 0; i < resolution; i++)
        {
            var prev = noiseValues[(i - 1 + resolution) % resolution];
            var curr = noiseValues[i];
            var next = noiseValues[(i + 1) % resolution];
            smoothed[i] = prev * 0.25f + curr * 0.5f + next * 0.25f;
        }

        // Add 2-4 cloud "puffs" (outward bumps)
        var puffCount = _random.Next(2, 5);
        for (var p = 0; p < puffCount; p++)
        {
            var puffAngle = _random.NextFloat(0, MathF.Tau);
            var puffWidth = _random.NextFloat(0.3f, 0.6f);
            var puffSize = _random.NextFloat(0.3f, 0.7f);

            for (var i = 0; i < resolution; i++)
            {
                var angle = i * angleStep;
                var diff = angle - puffAngle;
                while (diff < -MathF.PI) diff += MathF.Tau;
                while (diff >= MathF.PI) diff -= MathF.Tau;

                var bump = MathF.Exp(-diff * diff / (puffWidth * puffWidth)) * puffSize;
                smoothed[i] += bump;
            }
        }

        for (var i = 0; i < resolution; i++)
        {
            var radius = baseRadius * smoothed[i];
            radius = MathF.Max(baseRadius * 0.15f, radius);
            points.Add(new BiomeBoundaryPoint(i * angleStep, radius));
        }

        return points;
    }

    /// <summary>
    /// Fjord: narrow winding shape with inward bays.
    /// </summary>
    private List<BiomeBoundaryPoint> GenerateFjord(float baseRadius, int resolution)
    {
        var points = new List<BiomeBoundaryPoint>(resolution);
        var angleStep = MathF.Tau / resolution;
        var fjordAngle = _random.NextFloat(0, MathF.Tau);
        var inletDepth = _random.NextFloat(0.4f, 0.7f);

        for (var i = 0; i < resolution; i++)
        {
            var angle = i * angleStep;
            var relAngle = angle - fjordAngle;
            while (relAngle < 0) relAngle += MathF.Tau;
            while (relAngle >= MathF.Tau) relAngle -= MathF.Tau;

            // Main channel is narrow, wider at ends
            var channelWidth = 0.15f + _random.NextFloat(-0.03f, 0.03f);
            var cosRel = MathF.Cos(relAngle);
            var radius = baseRadius * channelWidth / (channelWidth + (1 - channelWidth) * cosRel * cosRel + 0.05f);

            // Inlet bays on sides
            var bayFactor = MathF.Sin(relAngle * 3 + _random.NextFloat(-0.5f, 0.5f)) * inletDepth;
            radius += baseRadius * MathF.Max(0, bayFactor) * 0.3f;

            var noise = 1f + _random.NextFloat(-0.05f, 0.05f);
            radius = MathF.Max(baseRadius * 0.05f, radius * noise);

            points.Add(new BiomeBoundaryPoint(angle, radius));
        }

        return points;
    }

    /// <summary>
    /// Archipelago: main blob with smaller satellite blobs.
    /// </summary>
    private List<BiomeBoundaryPoint> GenerateArchipelago(float baseRadius, int resolution)
    {
        var points = new List<BiomeBoundaryPoint>(resolution);
        var angleStep = MathF.Tau / resolution;

        // Main body
        var noiseValues = new float[resolution];
        for (var i = 0; i < resolution; i++)
            noiseValues[i] = 0.7f + _random.NextFloat(-0.1f, 0.1f);

        var smoothed = new float[resolution];
        for (var i = 0; i < resolution; i++)
        {
            var prev = noiseValues[(i - 1 + resolution) % resolution];
            var curr = noiseValues[i];
            var next = noiseValues[(i + 1) % resolution];
            smoothed[i] = prev * 0.25f + curr * 0.5f + next * 0.25f;
        }

        // Add 1-3 satellite islands
        var islandCount = _random.Next(1, 4);
        for (var s = 0; s < islandCount; s++)
        {
            var islandAngle = _random.NextFloat(0, MathF.Tau);
            var islandDist = _random.NextFloat(1.0f, 1.6f) * baseRadius;
            var islandRadius = _random.NextFloat(0.15f, 0.35f) * baseRadius;
            var islandSpread = _random.NextFloat(0.2f, 0.4f);

            for (var i = 0; i < resolution; i++)
            {
                var angle = i * angleStep;
                var px = MathF.Cos(angle) * baseRadius * smoothed[i];
                var py = MathF.Sin(angle) * baseRadius * smoothed[i];

                var dx = MathF.Cos(islandAngle) * islandDist - px;
                var dy = MathF.Sin(islandAngle) * islandDist - py;
                var dist = MathF.Sqrt(dx * dx + dy * dy);

                if (dist < islandRadius * (1 + islandSpread))
                {
                    var influence = MathF.Max(0, 1 - dist / (islandRadius * (1 + islandSpread)));
                    smoothed[i] = MathF.Max(smoothed[i], influence * islandRadius / baseRadius);
                }
            }
        }

        for (var i = 0; i < resolution; i++)
        {
            var radius = baseRadius * smoothed[i];
            radius = MathF.Max(baseRadius * 0.05f, radius);
            points.Add(new BiomeBoundaryPoint(i * angleStep, radius));
        }

        return points;
    }

    /// <summary>
    /// Spiral: swirling vortex shape.
    /// </summary>
    private List<BiomeBoundaryPoint> GenerateSpiral(float baseRadius, int resolution)
    {
        var points = new List<BiomeBoundaryPoint>(resolution);
        var angleStep = MathF.Tau / resolution;
        var spiralAngle = _random.NextFloat(0, MathF.Tau);
        var armCount = _random.Next(1, 3);
        var armTightness = _random.NextFloat(0.3f, 0.8f);

        for (var i = 0; i < resolution; i++)
        {
            var angle = i * angleStep;
            var relAngle = angle - spiralAngle;
            while (relAngle < 0) relAngle += MathF.Tau;
            while (relAngle >= MathF.Tau) relAngle -= MathF.Tau;

            // Base radius grows with angle for spiral effect
            var spiralR = baseRadius * (0.3f + 0.7f * (relAngle / MathF.Tau));

            // Add arms
            var armRadius = 0f;
            for (var a = 0; a < armCount; a++)
            {
                var armAngle = a * MathF.Tau / armCount;
                var diff = relAngle - armAngle;
                while (diff < -MathF.PI) diff += MathF.Tau;
                while (diff >= MathF.PI) diff -= MathF.Tau;

                var armFactor = MathF.Exp(-diff * diff / (armTightness * armTightness));
                armRadius = MathF.Max(armRadius, armFactor * baseRadius * 0.5f);
            }

            var radius = spiralR * 0.5f + armRadius;
            var noise = 1f + _random.NextFloat(-0.1f, 0.1f);
            radius = MathF.Max(baseRadius * 0.05f, radius * noise);

            points.Add(new BiomeBoundaryPoint(angle, radius));
        }

        return points;
    }

    /// <summary>
    /// Spawns the actual biome source entity with its irregular boundary shape.
    /// </summary>
    private void SpawnBiomeEntity(SpawnedBiomeSource source, MapId mapId)
    {
        var sourceId = source.BiomeId switch
        {
            "AsteroidBelt" => "SpaceBiomeSourceAsteroidBelt",
            "DebrisField" => "SpaceBiomeSourceDebrisField",
            "AnomalousSpace" => "SpaceBiomeSourceAnomalousSpace",
            _ => "SpaceBiomeSourceDefault"
        };

        var uid = Spawn(sourceId, new MapCoordinates(source.Position, mapId));

        if (TryComp<SpaceBiomeSourceComponent>(uid, out var comp))
        {
            comp.SwapDistance = FixedBiomeRadius;
            comp.Priority = source.Priority;

            // Store the boundary shape on the component for server-side containment checks
            comp.BoundaryResolution = BoundaryResolution;
            comp.BoundaryPoints = new float[source.BoundaryPoints.Count];
            for (var i = 0; i < source.BoundaryPoints.Count; i++)
            {
                // Store as multiplier (radius / fixed radius)
                comp.BoundaryPoints[i] = source.BoundaryPoints[i].Radius / FixedBiomeRadius;
            }

            Dirty(uid, comp);
        }
    }
}
