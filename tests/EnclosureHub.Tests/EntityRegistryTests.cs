using EnclosureHub.Core;
using EnclosureHub.Core.Entities;

namespace EnclosureHub.Tests;

public class EntityRegistryTests
{
    [Fact]
    public void Finds_entities_case_insensitively()
    {
        var sensor = new SensorEntity("cpu_temperature", "CPU temperature");
        var registry = new EntityRegistry([sensor]);

        Assert.Same(sensor, registry.Find("CPU_Temperature"));
        Assert.Null(registry.Find("missing"));
    }

    [Fact]
    public void Rejects_duplicate_ids()
    {
        var a = new SensorEntity("temp", "A");
        var b = new BinarySensorEntity("temp", "B");

        Assert.Throws<InvalidOperationException>(() => new EntityRegistry([a, b]));
    }

    [Fact]
    public void Raises_state_changed_only_on_change()
    {
        var sensor = new SensorEntity("temp", "Temp");
        int raised = 0;
        sensor.StateChanged += (_, _) => raised++;

        sensor.SetValue(21.5);
        sensor.SetValue(21.5);
        sensor.SetValue(22.0);

        Assert.Equal(2, raised);
    }
}
