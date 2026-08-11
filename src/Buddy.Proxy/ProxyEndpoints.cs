using System.Text.Json;
using System.Text.Json.Nodes;

namespace Buddy.Proxy;

public static class ProxyEndpoints
{
    private const int ReservationOverheadTokens = 512;

    public static void MapBuddyProxy(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGet(
            "/healthz",
            () => Results.Ok(
                new
                {
                    status = "healthy",
                    service = "buddy-proxy",
                    schema = ProxyDatabase.SchemaVersion,
                }));
        RouteGroupBuilder api = app.MapGroup("/v1")
            .RequireRateLimiting("proxy-api");
        api.MapGet("/quota", GetQuotaAsync);
        api.MapGet("/models", GetModelsAsync);
        api.MapPost("/chat/completions", ForwardChatAsync);
        app.MapPost("/v1/feedback", SubmitFeedbackAsync)
            .DisableAntiforgery()
            .RequireRateLimiting("feedback-api");
        app.MapPost("/chat/completions", ForwardChatAsync)
            .RequireRateLimiting("proxy-api");
    }

    public static int EstimateInputReservation(JsonObject payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        JsonObject clone = (JsonObject)payload.DeepClone();
        clone.Remove("max_tokens");
        int serializedBytes = JsonSerializer.SerializeToUtf8Bytes(clone).Length;
        return checked(serializedBytes + ReservationOverheadTokens);
    }

    private static async Task<IResult> GetQuotaAsync(
        HttpContext context,
        ProxyAuthentication authentication)
    {
        AuthenticationResult auth = await authentication
            .AuthenticateAsync(context.Request, context.RequestAborted)
            .ConfigureAwait(false);
        if (auth.Failure != AuthenticationFailure.None)
        {
            return AuthenticationError(auth.Failure);
        }

        AddQuotaHeaders(context.Response, auth.Client!);
        return Results.Ok(ProxyQuotaResponse.FromClient(auth.Client!));
    }

    private static async Task<IResult> GetModelsAsync(
        HttpContext context,
        ProxyAuthentication authentication,
        ProxyOptions options)
    {
        AuthenticationResult auth = await authentication
            .AuthenticateAsync(context.Request, context.RequestAborted)
            .ConfigureAwait(false);
        if (auth.Failure != AuthenticationFailure.None)
        {
            return AuthenticationError(auth.Failure);
        }

        AddQuotaHeaders(context.Response, auth.Client!);
        return Results.Ok(
            new
            {
                @object = "list",
                data = options.AllowedModels.Select(
                    model => new
                    {
                        id = model,
                        @object = "model",
                        owned_by = "deepseek",
                    }),
                quota = ProxyQuotaResponse.FromClient(auth.Client!),
            });
    }

    private static async Task<IResult> ForwardChatAsync(
        HttpContext context,
        ProxyAuthentication authentication,
        ProxyDatabase database,
        ClientRequestLock requestLock,
        DeepSeekGateway gateway,
        ProxyOptions options)
    {
        AuthenticationResult auth = await authentication
            .AuthenticateAsync(context.Request, context.RequestAborted)
            .ConfigureAwait(false);
        if (auth.Failure != AuthenticationFailure.None)
        {
            return AuthenticationError(auth.Failure);
        }

        if (context.Request.ContentLength > options.MaximumRequestBytes)
        {
            return Error(
                StatusCodes.Status413PayloadTooLarge,
                ProxyErrorCodes.InvalidRequest,
                "The request is larger than this proxy permits.");
        }

        JsonObject payload;
        try
        {
            JsonNode? parsed = await JsonNode
                .ParseAsync(
                    context.Request.Body,
                    documentOptions: default,
                    cancellationToken: context.RequestAborted)
                .ConfigureAwait(false);
            payload = parsed as JsonObject
                ?? throw new JsonException("The request root must be an object.");
        }
        catch (JsonException)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                ProxyErrorCodes.InvalidRequest,
                "The request body must be a valid JSON object.");
        }

        int bodyBytes = JsonSerializer.SerializeToUtf8Bytes(payload).Length;
        if (bodyBytes > options.MaximumRequestBytes)
        {
            return Error(
                StatusCodes.Status413PayloadTooLarge,
                ProxyErrorCodes.InvalidRequest,
                "The request is larger than this proxy permits.");
        }

        bool stream = false;
        if (payload.TryGetPropertyValue("stream", out JsonNode? streamNode)
            && streamNode is not null
            && (streamNode is not JsonValue streamValue
                || !streamValue.TryGetValue(out stream)))
        {
            return Error(
                StatusCodes.Status422UnprocessableEntity,
                ProxyErrorCodes.InvalidRequest,
                "The stream field must be a Boolean value.");
        }

        if (stream)
        {
            return Error(
                StatusCodes.Status422UnprocessableEntity,
                ProxyErrorCodes.StreamingUnsupported,
                "Streaming is disabled so exact token usage can be charged atomically.");
        }

        string? model = null;
        if (payload.TryGetPropertyValue("model", out JsonNode? modelNode)
            && modelNode is not null
            && (modelNode is not JsonValue modelValue
                || !modelValue.TryGetValue(out model)))
        {
            return Error(
                StatusCodes.Status422UnprocessableEntity,
                ProxyErrorCodes.InvalidRequest,
                "The model field must be a string.");
        }

        if (string.IsNullOrWhiteSpace(model)
            || !options.AllowedModels.Contains(model, StringComparer.Ordinal))
        {
            return Error(
                StatusCodes.Status422UnprocessableEntity,
                ProxyErrorCodes.ModelUnavailable,
                "The requested model is not available through this proxy.");
        }

        if (payload["messages"] is not JsonArray { Count: > 0 })
        {
            return Error(
                StatusCodes.Status422UnprocessableEntity,
                ProxyErrorCodes.InvalidRequest,
                "At least one chat message is required.");
        }

        await using IAsyncDisposable lease = await requestLock
            .EnterAsync(auth.Client!.Id, context.RequestAborted)
            .ConfigureAwait(false);
        ProxyClient client = await database
            .FindClientByIdAsync(auth.Client.Id, context.RequestAborted)
            .ConfigureAwait(false)
            ?? auth.Client;
        if (client.State != ProxyKeyState.Active)
        {
            return AuthenticationError(AuthenticationFailure.Disabled);
        }

        if (client.RepliesRemaining <= 0)
        {
            AddQuotaHeaders(context.Response, client);
            return Error(
                StatusCodes.Status429TooManyRequests,
                ProxyErrorCodes.ReplyQuotaExhausted,
                "This proxy key has used all of its dialog replies.");
        }

        int reservation = EstimateInputReservation(payload);
        long completionBudget = client.TokensRemaining - reservation;
        if (completionBudget < 1)
        {
            AddQuotaHeaders(context.Response, client);
            return Error(
                StatusCodes.Status429TooManyRequests,
                ProxyErrorCodes.TokenQuotaExhausted,
                "This proxy key does not have enough tokens for the request.");
        }

        int requestedMaximum = options.MaximumCompletionTokens;
        if (payload.TryGetPropertyValue("max_tokens", out JsonNode? maximumNode)
            && maximumNode is not null)
        {
            if (maximumNode is not JsonValue maxValue
                || !maxValue.TryGetValue(out int parsedMaximum)
                || parsedMaximum <= 0)
            {
                return Error(
                    StatusCodes.Status422UnprocessableEntity,
                    ProxyErrorCodes.InvalidRequest,
                    "The max_tokens field must be a positive integer.");
            }

            requestedMaximum = parsedMaximum;
        }

        int enforcedMaximum = (int)Math.Min(
            Math.Min(requestedMaximum, options.MaximumCompletionTokens),
            completionBudget);
        payload["max_tokens"] = enforcedMaximum;
        payload["stream"] = false;
        payload["user_id"] = $"buddy_{client.Id}";

        string requestId = Guid.NewGuid().ToString("N");
        GatewayResult result = await gateway
            .SendAsync(payload, requestId, context.RequestAborted)
            .ConfigureAwait(false);
        if (result.Failure is not null)
        {
            if (result.Failure.RetryAfter.HasValue)
            {
                context.Response.Headers.RetryAfter =
                    Math.Ceiling(result.Failure.RetryAfter.Value.TotalSeconds)
                        .ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return Error(
                result.Failure.StatusCode,
                result.Failure.Code,
                result.Failure.Message);
        }

        GatewaySuccess success = result.Success
            ?? throw new InvalidOperationException("The gateway returned no result.");
        ProxyClient updated = await database
            .RecordUsageAsync(
                client,
                new ProxyUsage(
                    requestId,
                    success.Model,
                    success.PromptTokens,
                    success.CompletionTokens,
                    success.CountsAsReply),
                context.RequestAborted)
            .ConfigureAwait(false);
        AddQuotaHeaders(context.Response, updated);
        context.Response.Headers["X-Buddy-Request-Id"] = requestId;
        return Results.Bytes(
            success.Body,
            contentType: "application/json; charset=utf-8");
    }

    private static async Task<IResult> SubmitFeedbackAsync(
        HttpContext context,
        ProxyAuthentication authentication,
        TelegramFeedbackGateway gateway)
    {
        AuthenticationResult auth = await authentication
            .AuthenticateAsync(context.Request, context.RequestAborted)
            .ConfigureAwait(false);
        if (auth.Failure != AuthenticationFailure.None)
        {
            return AuthenticationError(auth.Failure);
        }

        if (!context.Request.HasFormContentType
            || context.Request.ContentType is null
            || !context.Request.ContentType.StartsWith(
                "multipart/form-data",
                StringComparison.OrdinalIgnoreCase))
        {
            return Error(
                StatusCodes.Status415UnsupportedMediaType,
                ProxyErrorCodes.FeedbackInvalid,
                "Feedback must use multipart/form-data.");
        }

        if (context.Request.ContentLength > FeedbackLimits.MaximumRequestBytes)
        {
            return Error(
                StatusCodes.Status413PayloadTooLarge,
                ProxyErrorCodes.FeedbackInvalid,
                "The feedback request is larger than this proxy permits.");
        }

        IFormCollection form;
        try
        {
            form = await context.Request
                .ReadFormAsync(context.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return Error(
                StatusCodes.Status413PayloadTooLarge,
                ProxyErrorCodes.FeedbackInvalid,
                "The feedback request is larger than this proxy permits.");
        }
        catch (BadHttpRequestException)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                ProxyErrorCodes.FeedbackInvalid,
                "The feedback form is malformed.");
        }

        string message = form["message"].ToString().Trim();
        if (message.Length is 0 or > FeedbackLimits.MaximumMessageCharacters)
        {
            return Error(
                StatusCodes.Status422UnprocessableEntity,
                ProxyErrorCodes.FeedbackInvalid,
                $"Feedback must contain between 1 and {FeedbackLimits.MaximumMessageCharacters} characters.");
        }

        if (form.Files.Count > 1)
        {
            return Error(
                StatusCodes.Status422UnprocessableEntity,
                ProxyErrorCodes.FeedbackInvalid,
                "Attach at most one screenshot.");
        }

        IFormFile? file = form.Files.GetFile("screenshot");
        if (form.Files.Count == 1 && file is null)
        {
            return Error(
                StatusCodes.Status422UnprocessableEntity,
                ProxyErrorCodes.FeedbackInvalid,
                "The optional image must use the screenshot field.");
        }

        FeedbackScreenshot? screenshot = null;
        if (file is not null)
        {
            if (file.Length is <= 0 or > FeedbackLimits.MaximumScreenshotBytes)
            {
                return Error(
                    StatusCodes.Status413PayloadTooLarge,
                    ProxyErrorCodes.FeedbackInvalid,
                    $"The screenshot must be no larger than {FeedbackLimits.MaximumScreenshotBytes / 1024 / 1024} MB.");
            }

            byte[] bytes = await ReadBoundedAsync(
                    file,
                    FeedbackLimits.MaximumScreenshotBytes,
                    context.RequestAborted)
                .ConfigureAwait(false);
            string? contentType = FeedbackLimits.DetectImageContentType(bytes);
            if (contentType is null)
            {
                return Error(
                    StatusCodes.Status422UnprocessableEntity,
                    ProxyErrorCodes.FeedbackInvalid,
                    "The screenshot must be a PNG, JPEG, or WebP image.");
            }

            screenshot = new FeedbackScreenshot(bytes, contentType);
        }

        string feedbackId = $"FB-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..20];
        FeedbackSubmission submission = new(
            feedbackId,
            message,
            FeedbackLimits.NormalizeMetadata(form["app_version"], "unknown"),
            FeedbackLimits.NormalizeMetadata(form["interface_language"], "unknown"),
            FeedbackLimits.NormalizeMetadata(form["dialog_language"], "unknown"),
            DateTimeOffset.UtcNow,
            screenshot);

        try
        {
            FeedbackDeliveryResult result = await gateway
                .DeliverAsync(submission, auth.Client!, context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Ok(
                new
                {
                    feedback_id = result.FeedbackId,
                    screenshot_delivered = result.ScreenshotDelivered,
                });
        }
        catch (FeedbackDeliveryUnavailableException)
        {
            return Error(
                StatusCodes.Status503ServiceUnavailable,
                ProxyErrorCodes.FeedbackUnavailable,
                "Feedback delivery is temporarily unavailable.");
        }
        catch (FeedbackDeliveryException)
        {
            return Error(
                StatusCodes.Status502BadGateway,
                ProxyErrorCodes.FeedbackDeliveryFailed,
                "Feedback could not be delivered. Please try again.");
        }
    }

    private static IResult AuthenticationError(AuthenticationFailure failure)
    {
        return failure == AuthenticationFailure.Disabled
            ? Error(
                StatusCodes.Status403Forbidden,
                ProxyErrorCodes.DisabledKey,
                "This proxy key has been disabled.")
            : Error(
                StatusCodes.Status401Unauthorized,
                ProxyErrorCodes.InvalidKey,
                "The proxy key is invalid.");
    }

    private static IResult Error(int statusCode, string code, string message) =>
        Results.Json(
            new
            {
                error = new
                {
                    message,
                    type = "buddy_proxy_error",
                    param = (string?)null,
                    code,
                },
            },
            statusCode: statusCode);

    private static void AddQuotaHeaders(HttpResponse response, ProxyClient client)
    {
        response.Headers["X-Buddy-Quota-Replies-Limit"] =
            client.ReplyLimit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        response.Headers["X-Buddy-Quota-Replies-Remaining"] =
            client.RepliesRemaining.ToString(System.Globalization.CultureInfo.InvariantCulture);
        response.Headers["X-Buddy-Quota-Tokens-Limit"] =
            client.TokenLimit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        response.Headers["X-Buddy-Quota-Tokens-Remaining"] =
            client.TokensRemaining.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<byte[]> ReadBoundedAsync(
        IFormFile file,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using Stream input = file.OpenReadStream();
        using MemoryStream output = new((int)Math.Min(file.Length, maximumBytes));
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        if (output.Length > maximumBytes)
        {
            throw new InvalidDataException("The uploaded screenshot is too large.");
        }

        return output.ToArray();
    }

}
