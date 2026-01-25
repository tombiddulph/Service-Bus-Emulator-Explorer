using System.Text.RegularExpressions;
using Azure;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace ServiceBusEmulatorExplorer;

public sealed class ServiceBusExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        if (exception is ServiceBusException serviceBusException)
        {
            var (statusCode, title, detail) = MapServiceBusFailure(serviceBusException);
            var problemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = detail
            };

            await WriteProblemDetailsAsync(httpContext, problemDetails, exception);
            return true;
        }

        if (exception is RequestFailedException requestFailedException)
        {
            var statusCode = requestFailedException.Status;
            var title = ReasonPhrases.GetReasonPhrase(statusCode);
            var problemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = string.IsNullOrWhiteSpace(title) ? "Bad Request" : title,
                Detail = FormatServiceBusDetail(requestFailedException.Message)
            };

            await WriteProblemDetailsAsync(httpContext, problemDetails, exception);
            return true;
        }

        if (exception is ArgumentException argumentException)
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Bad Request",
                Detail = FormatServiceBusDetail(argumentException.Message)
            };

            await WriteProblemDetailsAsync(httpContext, problemDetails, exception);
            return true;
        }

        return false;
    }

    private static (int statusCode, string title, string detail) MapServiceBusFailure(ServiceBusException exception)
    {
        var (statusCode, title) = exception.Reason switch
        {
            ServiceBusFailureReason.MessagingEntityAlreadyExists => (StatusCodes.Status409Conflict, "Conflict"),
            ServiceBusFailureReason.MessagingEntityNotFound => (StatusCodes.Status404NotFound, "Not Found"),
            ServiceBusFailureReason.MessagingEntityDisabled => (StatusCodes.Status409Conflict, "Conflict"),
            ServiceBusFailureReason.MessageSizeExceeded => (StatusCodes.Status413PayloadTooLarge, "Payload Too Large"),
            ServiceBusFailureReason.QuotaExceeded => (StatusCodes.Status429TooManyRequests, "Quota Exceeded"),
            ServiceBusFailureReason.ServiceBusy => (StatusCodes.Status503ServiceUnavailable, "Service Unavailable"),
            ServiceBusFailureReason.ServiceTimeout => (StatusCodes.Status504GatewayTimeout, "Gateway Timeout"),
      
            _ => (StatusCodes.Status400BadRequest, exception.Reason.ToString()),
        };

        var detail = exception.Reason switch
        {
            ServiceBusFailureReason.MessagingEntityAlreadyExists => "Entity already exists.",
            ServiceBusFailureReason.MessagingEntityNotFound => "Entity not found.",
            ServiceBusFailureReason.MessagingEntityDisabled => "Entity is disabled.",
            ServiceBusFailureReason.MessageSizeExceeded => "Message size exceeds the allowed limit.",
            ServiceBusFailureReason.QuotaExceeded => "The Service Bus quota has been exceeded.",
            ServiceBusFailureReason.ServiceBusy => "Service Bus is busy. Try again later.",
            ServiceBusFailureReason.ServiceTimeout => "Service Bus timed out. Try again.",
            _ => FormatServiceBusDetail(exception.Message)
        };

        return (statusCode, title, detail);
    }

    private static string FormatServiceBusDetail(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Service Bus request failed.";
        }

        var detailMatch = Regex.Match(message, "<Detail>(?<detail>.*)</Detail>", RegexOptions.Singleline);
        var detail = detailMatch.Success ? detailMatch.Groups["detail"].Value : message;
        detail = Regex.Replace(detail, "SubCode=\\d+\\.?\\s*", string.Empty, RegexOptions.IgnoreCase);
        detail = StripDetailSuffix(detail, "TrackingId:");
        detail = StripDetailSuffix(detail, "SystemTracker:");
        detail = StripDetailSuffix(detail, "Timestamp:");
        detail = StripDetailSuffix(detail, "Content:");
        detail = detail.Trim();
        if (detail.Length <= 220)
        {
            return detail;
        }

        return detail[..217] + "...";
    }

    private static string StripDetailSuffix(string detail, string token)
    {
        var index = detail.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        return index >= 0 ? detail[..index].Trim() : detail;
    }

    private static async Task WriteProblemDetailsAsync(HttpContext httpContext, ProblemDetails problemDetails, Exception exception)
    {
        httpContext.Response.StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;

        var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception
        });
    }
}
