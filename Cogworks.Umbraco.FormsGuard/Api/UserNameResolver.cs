using Cogworks.Umbraco.FormsGuard.Persistence;
using Umbraco.Cms.Core.Services;

namespace Cogworks.Umbraco.FormsGuard.Api;

/// <summary>Maps audit actors and reviewers (backoffice user keys or <c>system</c>) to display names.</summary>
public interface IUserNameResolver
{
    /// <summary>
    /// One batch lookup for every distinct actor. <c>system</c> maps to "Forms Guard"; a key with no user, or a value
    /// that is not a GUID, maps to itself. Null and empty actors are left out.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> ResolveAsync(IEnumerable<string?> actors);
}

public sealed class UserNameResolver : IUserNameResolver
{
    public const string SystemName = "Forms Guard";

    private readonly IUserService _userService;

    public UserNameResolver(IUserService userService) => _userService = userService;

    public async Task<IReadOnlyDictionary<string, string>> ResolveAsync(IEnumerable<string?> actors)
    {
        var distinct = actors
            .Where(a => !string.IsNullOrEmpty(a))
            .Select(a => a!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var names = distinct.ToDictionary(a => a, a => a, StringComparer.Ordinal);
        foreach (var actor in distinct.Where(a => a == DecisionRepository.SystemActor))
        {
            names[actor] = SystemName;
        }

        var keys = distinct
            .Select(a => (Actor: a, Ok: Guid.TryParse(a, out var key), Key: key))
            .Where(x => x.Ok)
            .ToList();
        if (keys.Count == 0)
        {
            return names;
        }

        var users = await _userService.GetAsync(keys.Select(k => k.Key).Distinct());
        var byKey = users
            .Where(u => !string.IsNullOrWhiteSpace(u.Name))
            .GroupBy(u => u.Key)
            .ToDictionary(g => g.Key, g => g.First().Name!);
        foreach (var (actor, _, key) in keys)
        {
            if (byKey.TryGetValue(key, out var name))
            {
                names[actor] = name;
            }
        }

        return names;
    }
}
