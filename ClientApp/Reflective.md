## Reflective summary — Reduce redundant API calls (Product caching)

Date: 2025-10-01

This document summarizes the recent front-end changes made to reduce redundant API calls, why they were done, verification performed, and recommended next steps.

## Problem

The `FetchProducts.razor` page (and potentially other parts of the client) fetched product data directly via `HttpClient.GetFromJsonAsync(...)`. That approach can lead to:

- Duplicate HTTP calls when multiple components/pages request the same data concurrently.
- Repeated network calls on navigation or after trivial UI updates.
- Scattered model definitions and duplication of HTTP logic across components.

## What I changed

1. Introduced a single responsibility service for product data:
   - `Services/ProductService.cs` (implements `IProductService`)
     - In-memory cache of `Product[]`.
     - Request deduplication: concurrent callers await the same in-flight fetch task instead of creating multiple HTTP requests.
     - Timeout handling: uses a 10s timeout for the underlying request.
     - `GetProductsAsync(forceRefresh = false)` allows callers to explicitly bypass the cache.
2. Moved product model classes to a shared file:
   - `Models/Product.cs` — Product and Category models used by the service and pages.
3. Registered the service in DI:
   - `Program.cs` updated to add the scoped `IProductService` implementation.
4. Updated `Pages/FetchProducts.razor` to use the `IProductService` rather than calling Http directly. Removed inline Product model in that file and added a Refresh handler.

These changes centralize fetching logic and make it easy for any component to reuse the cached data instead of making duplicate fetches.

## Design decisions and rationale

- In-memory cache (fast, simple): adequate for single-page lifetime caching and avoids adding storage dependencies. It is reset when the page/app reloads.
- Request deduplication: implemented using a private in-flight Task reference and a SemaphoreSlim to guard initialization. This avoids issuing multiple identical calls if many components request the same data at once.
- 10s timeout locked in the service to mirror the previous behavior in the page and to limit hanging calls.
- Kept the endpoint URL in the service as `http://localhost:5286/api/productlist` to preserve behavior. This can be changed to a relative URI to use the app's `HttpClient.BaseAddress` for cross-environment deployment.

## Trade-offs and limitations

- Cache is ephemeral (in-memory only). It does not persist across page reloads or browser sessions. If persistence is desired, localStorage/sessionStorage could be used.
- The endpoint is currently hard-coded in the service. For deployments, prefer a relative path like `/api/productlist` and rely on the registered `HttpClient` BaseAddress.
- No automatic cache invalidation other than `forceRefresh`. You may want time-based eviction or server-sent invalidation.
- The service performs minimal error handling and rethrows network exceptions. We surface errors to the UI for now.

## Verification performed

- Built the project after changes: `dotnet build` in the `ClientApp` folder.
  - Result: Build succeeded with 1 warning: `isLoading` is assigned but not used in `FetchProducts.razor` (UI state only).
- Manual code inspection confirms:
  - `FetchProducts.razor` no longer calls Http directly and imports `ClientApp.Models`.
  - `ProductService` contains the caching and deduplication logic.

## How to run / quick checks

Open a PowerShell terminal inside `ClientApp` and run:

```powershell
cd "c:\Users\Travel\Downloads\Umesh\Projects\FullStackApp\ClientApp"
# build
dotnet build
# run (for Blazor WASM hosted appropriately; adjust target if necessary)
dotnet run
```

Then navigate to the app and open the products page. To confirm deduping:

- Open the browser devtools Network tab and trigger multiple components to request products (or open the page in multiple tabs). You should see at most one request to the product endpoint when the cache is empty and concurrent requests are issued.
- Click the 'Refresh' button (if you add it to the UI) to force a refresh and confirm a new call occurs.

## Requirements coverage (mapping)

- Identify and reduce redundant API calls in the front-end: Done — `ProductService` centralizes calls and dedupes concurrent requests (Done).
- Preserve previous error/timeout behavior: Done — service applies a 10s timeout and exceptions surface to callers (Done).
- Avoid breaking existing UI: Done — `FetchProducts.razor` updated to consume service and still renders the same info (Done).

## Next steps (recommended)

1. Replace the hard-coded endpoint with a relative path and use the injected `HttpClient` base address in the service. This will make deployments easier.
2. Add a small UI refresh button and wire an in-UI spinner to the `isLoading` state in `FetchProducts.razor`.
3. Add optional persistence (sessionStorage/localStorage) or a time-based cache TTL in `ProductService`.
4. Add unit tests for `ProductService`:
   - Happy path (returns products)
   - Dedupe/concurrency test (multiple callers await single fetch)
   - Error handling test (when HTTP fails)
5. Consider adding logging for fetches and cache hits/misses for easier troubleshooting in production.

---

If you want, I can implement the relative-URI change (use the registered HttpClient's BaseAddress) and add the UI refresh button + spinner next. Which would you prefer me to do now?
