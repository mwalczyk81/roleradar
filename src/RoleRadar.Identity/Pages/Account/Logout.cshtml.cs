using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RoleRadar.Identity.Pages.Account;

[AllowAnonymous]
public class LogoutModel(
    SignInManager<IdentityUser<Guid>> signInManager,
    IIdentityServerInteractionService interactionService) : PageModel
{
    private readonly SignInManager<IdentityUser<Guid>> _signInManager = signInManager;
    private readonly IIdentityServerInteractionService _interactionService = interactionService;

    [BindProperty(SupportsGet = true)]
    public string? LogoutId { get; set; }

    public async Task<IActionResult> OnGetAsync(string? logoutId = null)
    {
        var id = logoutId ?? LogoutId;
        var logoutContext = !string.IsNullOrWhiteSpace(id)
            ? await _interactionService.GetLogoutContextAsync(id, HttpContext.RequestAborted)
            : null;

        // Hardening against CSRF / forced logout: only sign out when GetLogoutContextAsync returns a validated context.
        // With no validated context, render the signed-out page without signing out.
        if (logoutContext == null)
        {
            return Page();
        }

        return await ProcessLogoutAsync(logoutContext);
    }

    public async Task<IActionResult> OnPostAsync(string? logoutId = null)
    {
        var id = logoutId ?? LogoutId;
        var logoutContext = !string.IsNullOrWhiteSpace(id)
            ? await _interactionService.GetLogoutContextAsync(id, HttpContext.RequestAborted)
            : null;

        return await ProcessLogoutAsync(logoutContext);
    }

    private async Task<IActionResult> ProcessLogoutAsync(LogoutRequest? logoutContext)
    {
        await _signInManager.SignOutAsync();
        await HttpContext.SignOutAsync(IdentityConstants.TwoFactorRememberMeScheme);
        await HttpContext.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);

        if (!string.IsNullOrWhiteSpace(logoutContext?.PostLogoutRedirectUri))
        {
            // Only redirect to the URI Duende has already validated against registered PostLogoutRedirectUris.
            return Redirect(logoutContext.PostLogoutRedirectUri);
        }

        // If there is no valid logout context or no post-logout URI, show a simple "You are signed out" page.
        return Page();
    }
}
