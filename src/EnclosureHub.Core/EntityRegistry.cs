using EnclosureHub.Core.Entities;

namespace EnclosureHub.Core;

public interface IEntityRegistry
{
    IReadOnlyList<Entity> All { get; }

    Entity? Find(string id);
}

/// <summary>
/// Collects every <see cref="Entity"/> registered in DI by the enabled modules.
/// The set is fixed at startup, so adapters can safely enumerate it.
/// </summary>
public sealed class EntityRegistry : IEntityRegistry
{
    private readonly Dictionary<string, Entity> _byId;

    public EntityRegistry(IEnumerable<Entity> entities)
    {
        All = entities.ToList();
        _byId = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in All)
        {
            if (!_byId.TryAdd(entity.Id, entity))
                throw new InvalidOperationException($"Duplicate entity id '{entity.Id}'. Check module configuration.");
        }
    }

    public IReadOnlyList<Entity> All { get; }

    public Entity? Find(string id) => _byId.GetValueOrDefault(id);
}
