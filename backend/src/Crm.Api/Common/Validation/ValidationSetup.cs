using Crm.Application.Common;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Crm.Api.Common.Validation;

/// <summary>
/// Every incoming payload is validated before any business logic runs (Constitution III,
/// spec FR-019), and every failure is reported through the shared error contract.
///
/// The filter is what makes this true by construction: a new endpoint inherits validation without
/// its author remembering to call anything.
/// </summary>
public static class ValidationSetup
{
    public static IServiceCollection AddCrmValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddValidatorsFromAssembly(typeof(ErrorCodes).Assembly, includeInternalTypes: true);
        services.AddScoped<ValidationFilter>();

        services.Configure<MvcOptions>(options => options.Filters.AddService<ValidationFilter>());

        // Model binding failures (malformed JSON, wrong types, depth limits) must use the same
        // contract as rule failures, not the framework default.
        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var failures = context.ModelState
                    .Where(entry => entry.Value?.Errors.Count > 0)
                    .SelectMany(entry => entry.Value!.Errors.Select(error => new FieldFailure(
                        ToClientField(entry.Key),
                        ErrorCodes.Field.Format,
                        string.IsNullOrWhiteSpace(error.ErrorMessage)
                            ? "The value could not be read."
                            : error.ErrorMessage)))
                    .ToList();

                var problem = ValidationProblems.Build(
                    context.HttpContext,
                    failures,
                    ErrorCodes.MalformedRequest,
                    "The request could not be read.");

                return new BadRequestObjectResult(problem)
                {
                    ContentTypes = { "application/problem+json" },
                };
            };
        });

        return services;
    }

    /// <summary>Client-facing member paths are camelCase, matching the JSON the caller sent.</summary>
    internal static string ToClientField(string member)
    {
        if (string.IsNullOrEmpty(member))
        {
            return member;
        }

        return string.Join(
            '.',
            member.Split('.').Select(segment =>
                segment.Length == 0 ? segment : char.ToLowerInvariant(segment[0]) + segment[1..]));
    }
}

/// <summary>
/// Runs the registered validator for each action argument and short-circuits with the error
/// contract when any rule fails. Business logic never sees an invalid payload.
/// </summary>
public sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var failures = new List<FieldFailure>();

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());

            if (services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var validationContext = new ValidationContext<object>(argument);
            var result = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);

            failures.AddRange(result.Errors.Select(error => new FieldFailure(
                ValidationSetup.ToClientField(error.PropertyName),
                error.ErrorCode ?? ErrorCodes.Field.Format,
                error.ErrorMessage)));
        }

        if (failures.Count > 0)
        {
            var problem = ValidationProblems.Build(context.HttpContext, failures);

            context.Result = new BadRequestObjectResult(problem)
            {
                ContentTypes = { "application/problem+json" },
            };

            return;
        }

        await next();
    }
}
