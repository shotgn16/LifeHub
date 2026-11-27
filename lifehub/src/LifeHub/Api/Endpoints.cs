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

        // Settings → Appearance → Custom CSS, then the file custom.css in the data folder (both optional).
        app.MapGet("/custom.css", (SettingsService settings, DataPaths paths) =>
        {
            var css = settings.Household.General.CustomCss ?? "";
            if (File.Exists(paths.CustomCssFile)) css += "\n" + File.ReadAllText(paths.CustomCssFile);
            return Results.Text(css, "text/css");
        }).AllowAnonymous();

        MapAuth(app);
        MapApi(app);

        // Backup download (signed-in users only).
        app.MapGet("/backup/{name}", (string name, DataPaths paths) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^(lifehub|bible-export)-\d{8}-\d{6}\.zip$")) return Results.NotFound();
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

    private static Task SignInAsync(HttpContext ctx, UserProfile user, string via) =>
        ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            AuthConstants.Principal(user, CookieAuthenticationDefaults.AuthenticationScheme, via),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) });

    private static void MapAuth(WebApplication app)
    {
        app.MapPost("/auth/login", async (HttpContext ctx, [FromForm] string username, [FromForm] string password,
            [FromForm] string? returnUrl, SettingsService settings, LoginThrottle throttle) =>
        {
            var key = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (throttle.IsLocked(key)) return Results.Redirect($"{ctx.Request.PathBase}/login?error=locked");
            var user = settings.Users.FirstOrDefault(u => u.HasPassword && u.Username.Equals(username.Trim(), StringComparison.OrdinalIgnoreCase));
            if (user is null || !PasswordHash.Verify(password, user.PasswordHash))
            {
                throttle.Fail(key);
                await Task.Delay(800);
                return Results.Redirect($"{ctx.Request.PathBase}/login?error=1&returnUrl={Uri.EscapeDataString(returnUrl ?? "")}");
            }
            throttle.Success(key);
            await SignInAsync(ctx, user, "password");
            return Results.Redirect(SafeReturn(ctx, returnUrl));
        }).AllowAnonymous();

        app.MapPost("/auth/setup", async (HttpContext ctx, [FromForm] string username, [FromForm] string password,
            [FromForm] string confirm, [FromForm] string? name, [FromForm] string? returnUrl, SettingsService settings) =>
        {
            // First-run only; afterwards logins are managed in Settings → People / Security.
            if (settings.HasLogin)
                return Results.Redirect($"{ctx.Request.PathBase}/login");
            if (string.IsNullOrWhiteSpace(username) || password.Length < 8 || password != confirm)
                return Results.Redirect($"{ctx.Request.PathBase}/setup?error=1&returnUrl={Uri.EscapeDataString(returnUrl ?? "")}");
            await settings.UpdateAsync(s =>
            {
                var u = s.Users.FirstOrDefault(x => x.IsAdmin) ?? s.Users[0];
                u.Username = username.Trim();
                u.PasswordHash = PasswordHash.Create(password);
                if (!string.IsNullOrWhiteSpace(name)) u.Name = name.Trim();
                else if (u.Name is "" or "Me") u.Name = char.ToUpperInvariant(u.Username[0]) + u.Username[1..];
                if (string.IsNullOrEmpty(u.ApiKey)) u.ApiKey = PasswordHash.NewApiKey();
            });
            await SignInAsync(ctx, settings.User(settings.PrimaryUserId)!, "password");
            return Results.Redirect(SafeReturn(ctx, returnUrl ?? "/"));
        }).AllowAnonymous();

        // The desktop app window signs the main person in with a secret kept in the data folder (only from this computer).
        app.MapGet("/auth/desktop", async (HttpContext ctx, string? token, string? once, string? returnUrl, SettingsService settings) =>
        {
            if (LifeHub.Desktop.DesktopRuntime.TakeOnce(once) is { } created && settings.User(created) is { } person)
            {
                await SignInAsync(ctx, person, "password");
                return Results.Redirect(SafeReturn(ctx, returnUrl));
            }
            var ip = ctx.Connection.RemoteIpAddress;
            if (ip is null || !System.Net.IPAddress.IsLoopback(ip) || !LifeHub.Desktop.DesktopRuntime.CheckToken(token))
                return Results.Redirect($"{ctx.Request.PathBase}/login");
            var target = SafeReturn(ctx, returnUrl);
            if (!settings.HasLogin) return Results.Redirect(target);          // first run: the setup assistant creates the account
            var main = settings.User(settings.PrimaryUserId) ?? settings.Users.First(u => u.IsAdmin);
            await SignInAsync(ctx, main, "desktop");
            return Results.Redirect(target);
        }).AllowAnonymous();

        // Home Assistant "Who's using LifeHub?" — only inside HA (ingress), where HA has already signed someone in.
        app.MapPost("/auth/pick", async (HttpContext ctx, [FromForm] Guid user, [FromForm] string? pin, [FromForm] string? returnUrl,
            SettingsService settings, LoginThrottle throttle) =>
        {
            if (!ctx.Items.ContainsKey("ingress") || !settings.Household.Auth.TrustHomeAssistantIngress) return Results.Redirect($"{ctx.Request.PathBase}/login");
            var person = settings.User(user);
            if (person is null) return Results.Redirect($"{ctx.Request.PathBase}/who");
            var key = "pin:" + person.Id;
            if (!string.IsNullOrEmpty(person.PinHash))
            {
                if (throttle.IsLocked(key)) return Results.Redirect($"{ctx.Request.PathBase}/who?user={person.Id}&error=locked");
                if (string.IsNullOrEmpty(pin) || !PasswordHash.Verify(pin.Trim(), person.PinHash))
                {
                    throttle.Fail(key);
                    await Task.Delay(500);
                    return Results.Redirect($"{ctx.Request.PathBase}/who?user={person.Id}&error=1");
                }
                throttle.Success(key);
            }
            await SignInAsync(ctx, person, "ingress");
            return Results.Redirect(SafeReturn(ctx, returnUrl));
        }).AllowAnonymous();

        // The LifeHub phone apps sign in with the person's API key (kept on the phone), so nobody types a password.
        app.MapPost("/auth/app", async (HttpContext ctx, [FromForm] string key, [FromForm] string? returnUrl,
            SettingsService settings, LoginThrottle throttle) =>
        {
            var ip = "app:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            if (throttle.IsLocked(ip)) return Results.Redirect($"{ctx.Request.PathBase}/login?error=locked");
            var bytes = System.Text.Encoding.UTF8.GetBytes(key ?? "");
            var user = settings.Users.FirstOrDefault(u => !string.IsNullOrEmpty(u.ApiKey) &&
                System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(bytes, System.Text.Encoding.UTF8.GetBytes(u.ApiKey)));
            if (user is null)
            {
                throttle.Fail(ip);
                await Task.Delay(800);
                return Results.Redirect($"{ctx.Request.PathBase}/login?error=1");
            }
            throttle.Success(ip);
            await SignInAsync(ctx, user, "app");
            return Results.Redirect(SafeReturn(ctx, returnUrl));
        }).AllowAnonymous().DisableAntiforgery();

        app.MapGet("/auth/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect($"{ctx.Request.PathBase}/{(ctx.Items.ContainsKey("ingress") ? "who" : "login")}");
        }).AllowAnonymous();
    }

    public sealed record CaptureRequest(string Text);
    public sealed record TaskRequest(string Title, string? Category, DateOnly? Due, TimeOnly? Time, string? Priority, string? Notes);
    public sealed record ShoppingRequest(string Name, decimal? Quantity, string? Unit);
    public sealed record ScanRequest(string Barcode, string? Mode, decimal? Quantity);
    public sealed record NoteRequest(string Text, bool? Shared);
    public sealed record SyncRequest(List<SyncService.Change>? Changes);

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

        // Ask LifeHub: an answer, plus suggestions that wait for POST /suggestions/{id}/apply (or the app's AI Inbox).
        api.MapPost("/ask", async (CaptureRequest req, AskService ask) =>
        {
            if (!ask.IsAvailable) return Results.Problem("No AI is set up (Settings → AI)", statusCode: 503);
            var reply = await ask.AskAsync(req.Text, "api");
            return Results.Ok(new
            {
                answer = reply.Answer,
                suggestions = reply.Suggestions.Select(s => new { s.Id, s.Type, s.Title, s.Date, s.Time, status = s.Status.ToString() })
            });
        });

        api.MapPost("/suggestions/{id:guid}/apply", async (Guid id, AiService ai) =>
            await ai.ApplyAsync(id) is { } s ? Results.Ok(new { status = s.Status.ToString(), s.Result }) : Results.NotFound());

        api.MapPost("/suggestions/{id:guid}/skip", async (Guid id, AiService ai) =>
        {
            if (ai.Suggestions.Get(id) is null) return Results.NotFound();
            await ai.RejectAsync(id);
            return Results.Ok();
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

        // ---- Phone apps ----
        api.MapGet("/me", (SettingsService settings) =>
        {
            var me = settings.Me;
            var s = settings.Current;
            return me is null ? Results.Unauthorized() : Results.Ok(new
            {
                me.Id, me.Name, me.IsAdmin, version = typeof(Endpoints).Assembly.GetName().Version?.ToString(3),
                household = s.General.HouseholdName, deals = s.Deals.Enabled
            });
        });

        // Offline support for the phone apps: what to keep on the phone, and replaying changes made offline.
        api.MapGet("/offline", (SyncService sync, EmailSyncService email) => { email.SyncSoon("app"); return Results.Ok(sync.Snapshot()); });
        // The phone apps call this when they open (and on their background schedule).
        api.MapPost("/email/check", (EmailSyncService email) => { email.SyncSoon("app"); return Results.Ok(new { status = "started", last = email.LastRunUtc, summary = email.LastRunSummary }); });
        api.MapPost("/sync", async (SyncRequest req, SyncService sync) =>
        {
            var results = await sync.ApplyAsync(req.Changes ?? new());
            return Results.Ok(new { results, snapshot = sync.Snapshot() });
        });

        // New notifications for the phone apps to show (polled in the background).
        api.MapGet("/notices", (NotificationService notices, DateTime? since) =>
            Results.Ok(notices.Recent(30).Where(n => since is null || n.CreatedUtc > since.Value.ToUniversalTime())
                .OrderBy(n => n.CreatedUtc)
                .Select(n => new { n.Id, n.Title, n.Message, n.Link, level = n.Level.ToString(), n.CreatedUtc, n.Read, n.Category, n.Phone })));


        // ---- Notes ----
        api.MapPost("/notes", async (NoteRequest req, NotesService notes) =>
        {
            if (string.IsNullOrWhiteSpace(req.Text)) return Results.BadRequest(new { status = "empty" });
            var n = await notes.AddAsync(req.Text, req.Shared == true, "api");
            return Results.Ok(new { n.Id, n.Category, sorting = notes.AiAvailable });
        });

        api.MapGet("/notes", (NotesService notes, string? category, string? q) =>
            Results.Ok(notes.Search(q ?? "", string.IsNullOrWhiteSpace(category) ? null : category).Take(200)
                .Select(n => new { n.Id, n.Text, n.Title, n.Category, n.Tags, n.Pinned, n.Shared, n.CreatedUtc })));

        // ---- Meals ----
        api.MapGet("/meals", (MealService meals, Clock clock, int? days) =>
        {
            var from = clock.Today;
            var to = from.AddDays(Math.Clamp(days ?? 1, 1, MealService.MaxDays) - 1);
            return Results.Ok(meals.EntriesFor(from, to).Select(e => new { e.Date, e.Slot, title = meals.TitleOf(e), e.Servings, e.Cooked }));
        });

        api.MapGet("/period", async (ScheduleService schedule) =>
        {
            var p = await schedule.CurrentAsync();
            return Results.Ok(new { p.Label, kind = p.Kind?.ToString(), p.Until, p.NextLabel, p.NextStart, @event = p.Event?.Title });
        });
    }
}
