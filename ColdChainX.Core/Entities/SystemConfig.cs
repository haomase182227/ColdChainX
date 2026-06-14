using System;

namespace ColdChainX.Core.Entities;

public partial class SystemConfig
{
    public Guid Id { get; set; }

    public string Key { get; set; } = null!;

    public string Value { get; set; } = null!;

    public string? Description { get; set; }
}
