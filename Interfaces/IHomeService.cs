using System.Security.Claims;
using Hub.ViewModels;

namespace Hub.Interfaces;

public interface IHomeService
{
    /// <summary>Null when the caller's identity lacks the claims a hub session is built from.</summary>
    Task<HomeResponse?> GetHomeAsync(ClaimsPrincipal user);
}
