using System.ComponentModel.DataAnnotations;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RoleRadar.Identity.Pages.Account;

[AllowAnonymous]
public class LoginModel(
    SignInManager<IdentityUser<Guid>> signInManager,
    UserManager<IdentityUser<Guid>> userManager,
    IIdentityServerInteractionService interactionService) : PageModel
{
    private readonly SignInManager<IdentityUser<Guid>> _signInManager = signInManager;
    private readonly UserManager<IdentityUser<Guid>> _userManager = userManager;
    private readonly IIdentityServerInteractionService _interactionService = interactionService;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public TwoFactorModel TwoFactorInput { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    public bool RequiresTwoFactor { get; set; }

    public sealed class InputModel
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }

    public sealed class TwoFactorModel
    {
        [Required]
        [StringLength(7, MinimumLength = 6)]
        public string Code { get; set; } = string.Empty;
    }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? "/";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ReturnUrl ??= "/";

        if (RequiresTwoFactor)
        {
            if (string.IsNullOrWhiteSpace(TwoFactorInput.Code))
            {
                ModelState.AddModelError(string.Empty, "Please enter your 6-digit authenticator code.");
                return Page();
            }

            var cleanCode = TwoFactorInput.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
            var twoFactorResult = await _signInManager.TwoFactorAuthenticatorSignInAsync(
                cleanCode,
                isPersistent: false,
                rememberClient: false);

            if (twoFactorResult.Succeeded)
            {
                return ValidateAndRedirect(ReturnUrl);
            }

            if (twoFactorResult.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty, "Account is locked due to multiple failed login attempts. Please try again after 15 minutes.");
                return Page();
            }

            ModelState.AddModelError(string.Empty, "Invalid verification code.");
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Input.Username) || string.IsNullOrWhiteSpace(Input.Password))
        {
            ModelState.AddModelError(string.Empty, "Username and password are required.");
            return Page();
        }

        var result = await _signInManager.PasswordSignInAsync(
            Input.Username.Trim(),
            Input.Password,
            isPersistent: false,
            lockoutOnFailure: true);

        if (result.RequiresTwoFactor)
        {
            RequiresTwoFactor = true;
            return Page();
        }

        if (result.Succeeded)
        {
            return ValidateAndRedirect(ReturnUrl);
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "Account is locked due to multiple failed login attempts. Please try again after 15 minutes.");
            return Page();
        }

        ModelState.AddModelError(string.Empty, "Invalid username or password.");
        return Page();
    }

    private RedirectResult ValidateAndRedirect(string returnUrl)
    {
        if (_interactionService.IsValidReturnUrl(returnUrl) || Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return Redirect("~/");
    }
}
