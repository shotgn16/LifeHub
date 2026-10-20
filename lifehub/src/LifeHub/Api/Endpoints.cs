using System.Security.Claims;
using LifeHub.Ai;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace LifeHub.Api;

public static class Endpoints
{
    public static void MapLifeHubEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok", version = typeof(Endpoints).Assembly.GetName().Version?.ToString() }))
            .AllowAnonymous();

        MapAuth(app);
        MapApi(app);

        // Backup download (signed-in users only).
        app.MapGet("/backup/{name}", (string name, DataPaths paths) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^lifehub-\d{8}-\d{6}\.zip$")) return Results.NotFound();
            var file = Path.Combine(paths.Backups, name);
            return File.Exists(file) ? Results.File(file, "application/zip", name) : Results.NotFound();
        }).RequireAuthorization();
    }

    private static string SafeReturn(HttpContext ctx, string? returnUrl)
    {
        var baseHref = ctx.Request.PathBase.Value ?? "";
        if (string.IsNullOrEmpty(returnUrl) || !returnUrl.StartsWith('/') || returnUrl.StartsWith("//")) return baseHref + "/";
        return returnUrl.StartsWith(baseHref) ? returnUrl : baseHref + returnUrl;
    }

    private static void MapAuth(WebApplication app)
    {
        app.MapPost("/auth/login", async (HttpContext ctx, [FromForm] string username, [FromForm] string password,
            [FromForm] string? returnUrl, SettingsService settings, LoginThrottle throttle) =>
        {
            var key = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var auth = settings.Current.Auth;
            if (throttle.IsLocked(key)) return Results.Redirect($"{ctx.Request.PathBase}/login?error=locked");
            if (!auth.IsConfigured || !username.Trim().Equals(auth.Username, StringComparison.OrdinalIgnoreCase) || !PasswordHash.Verify(password, auth.PasswordHash))
            {
                throttle.Fail(key);
                await Task.Delay(800);
                return Results.Redirect($"{ctx.Request.PathBase}/login?error=1&returnUrl={Uri.EscapeDataString(returnUrl ?? "")}");
            }
            throttle.Success(key);
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                AuthConstants.Principal(auth.Username, CookieAuthenticationDefaults.AuthenticationScheme),
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) });
            return Results.Redirect(SafeReturn(ctx, returnUrl));
        }).AllowAnonymous();

        app.MapPost("/auth/setup", async (HttpContext ctx, [FromForm] string username, [FromForm] string password,
            [FromForm] string confirm, SettingsService settings) =>
        {
            // First-run only; afterwards the login is changed in Settings → Security (needs the current password).
            if (settings.Current.Auth.IsConfigured)
                return Results.Redirect($"{ctx.Request.PathBase}/login");
            if (string.IsNullOrWhiteSpace(username) || password.Length < 8 || password != confirm)
                return Results.Redirect($"{ctx.Request.PathBase}/setup?error=1");
            await settings.UpdateAsync(s =>
            {
                s.Auth.Username = username.Trim();
                s.Auth.PasswordHash = PasswordHash.Create(password);
                if (string.IsNullOrEmpty(s.Auth.ApiKey)) s.Auth.ApiKey = PasswordHash.NewApiKey();
            });
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                AuthConstants.Principal(username.Trim(), CookieAuthenticationDefaults.AuthenticationScheme),
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) });
            return Results.Redirect(SafeReturn(ctx, "/"));
        }).AllowAnonymous();

        app.MapGet("/auth/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect($"{ctx.Request.PathBase}/login");
        }).AllowAnonymous();
    }

    public sealed record CaptureRequest(string Text);
    public sealed record TaskRequest(string Title, string? Category, DateOnly? Due, TimeOnly? Time, string? Priority, string? Notes);
    public sealed record ShoppingRequest(string Name, decimal? Quantity, string? Unit);
    public sealed record ScanRequest(string Barcode, string? Mode, decimal? Quantity);

    /// <summary>
    /// REST API for Home Assistant automations, scripts, voice assistants, iOS Shortcuts, etc.
    /// Authenticate with the API key from Settings → Security (header X-Api-Key or Bearer token).
    /// </summary>
    private static void MapApi(WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization(p => p.AddAuthenticationSchemes(AuthConstants.ApiKeyScheme).RequireAuthenticatedUser())
            .DisableAntiforgery();

        api.MapGet("/summary", async (TaskService tasks, RenewalService renewals, KitchenService kitchen, ScheduleService schedule,
            AiService ai, SettingsService settings, Clock clock) =>
        {
            var period = await schedule.CurrentAsync();
            return Results.Ok(new
            {
                now = clock.Now,
                period = new { period.Label, kind = period.Kind?.ToString(), period.Until },
                tasksDue = tasks.DueToday().Select(t => new { t.Id, t.Title, t.Category, t.DueDate, t.DueTime, priority = t.Priority.ToString() }),
                overdue = tasks.OverdueCount(),
                renewalsDue = renewals.DueWithin(settings.Current.General.RenewalWarningDays).Select(r => new { r.Name, r.NextRenewal, r.Cost, r.Currency }),
                monthlySpend = Math.Round(renewals.MonthlyTotal(), 2),
                foodExpiring = kitchen.ExpiringWithin(settings.Current.General.ExpiryWarningDays).Select(p => new { p.Name, p.UseBy, location = p.Location.ToString() }),
                shopping = kitchen.Shopping.Where(s => !s.Checked).Select(s => s.Name),
                aiPending = ai.PendingCount
            });
        });

        api.MapPost("/capture", async (CaptureRequest req, AiService ai) =>
        {
            if (!ai.IsAvailable) return Results.Problem("AI (Ollama) is not configured", statusCode: 503);
            var list = await ai.CaptureAsync(req.Text, "api");
            return Results.Ok(list.Select(s => new { s.Type, s.Title, s.Date, s.Time, status = s.Status.ToString(), s.Result }));
        });

        api.MapGet("/tasks", (TaskService tasks, string? filter) =>
        {
            var list = filter == "today" ? tasks.DueToday() : tasks.Ordered(tasks.Open()).ToList();
            return Results.Ok(list.Select(t => new { t.Id, t.Title, t.Category, t.DueDate, t.DueTime, priority = t.Priority.ToString(), state = t.State.ToString() }));
        });

        api.MapPost("/tasks", async (TaskRequest req, TaskService tasks, SettingsService settings) =>
        {
            var t = new TaskItem
            {
                Title = req.Title, Category = req.Category ?? settings.Current.General.TaskCategories.FirstOrDefault() ?? "Admin",
                DueDate = req.Due, DueTime = req.Time, Notes = req.Notes ?? "", Source = "api",
                Priority = Enum.TryParse<TaskPriority>(req.Priority, true, out var p) ? p : TaskPriority.Normal
            };
            await tasks.SaveAsync(t);
            return Results.Created($"/api/v1/tasks/{t.Id}", new { t.Id });
        });

        api.MapPost("/tasks/{id:guid}/complete", async (Guid id, TaskService tasks) =>
        {
            await tasks.CompleteAsync(id);
            return Results.NoContent();
        });

        api.MapGet("/shopping", (KitchenService kitchen) =>
            Results.Ok(kitchen.Shopping.Where(s => !s.Checked).Select(s => new { s.Id, s.Name, s.Quantity, s.Unit, s.Reason })));

        api.MapPost("/shopping", async (ShoppingRequest req, KitchenService kitchen) =>
        {
            var item = await kitchen.AddToShoppingAsync(req.Name, req.Quantity ?? 1, req.Unit ?? "", "API");
            return Results.Ok(new { item.Id, item.Name, item.Quantity });
        });

        // For a USB/Bluetooth barcode scanner on a Pi, an ESPHome device, or an HA automation.
        api.MapPost("/scan", async (ScanRequest req, BarcodeService barcodes) =>
        {
            var mode = req.Mode?.ToLowerInvariant() switch { "use" => ScanMode.Use, "shopping" => ScanMode.Shopping, _ => ScanMode.Add };
            var r = await barcodes.HandleAsync(req.Barcode, mode, requireConfirmed: false, req.Quantity ?? 1);
            if (r.NeedsDetails)
                return Results.NotFound(new { status = "unknown", barcode = BarcodeService.Normalize(req.Barcode), message = "Unknown barcode — scan it once in the app to name it." });
            return Results.Ok(new { status = r.Success ? "ok" : r.NotInStock ? "not_in_stock" : "failed", r.Message, product = r.Product?.Name });
        });

        api.MapGet("/period", async (ScheduleService schedule) =>
        {
            var p = await schedule.CurrentAsync();
            return Results.Ok(new { p.Label, kind = p.Kind?.ToString(), p.Until, p.NextLabel, p.NextStart, @event = p.Event?.Title });
        });
    }
}
