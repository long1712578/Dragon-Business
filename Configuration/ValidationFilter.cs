using FluentValidation;

namespace Dragon.Business.Configuration;

public class ValidationFilter<T> : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var validator = context.HttpContext.RequestServices.GetService<IValidator<T>>();
        
        if (validator is not null)
        {
            // Find the argument of type T
            var argument = context.Arguments.OfType<T>().FirstOrDefault();
            if (argument is not null)
            {
                var validationResult = await validator.ValidateAsync(argument);
                if (!validationResult.IsValid)
                {
                    var errors = validationResult.Errors.Select(e => e.ErrorMessage).ToList();
                    return Results.BadRequest(new ErrorResponse("Validation failed", string.Join("; ", errors)));
                }
            }
        }

        return await next(context);
    }
}

// Extension to map validation filter
public static class ValidationExtensions
{
    public static RouteHandlerBuilder WithValidator<T>(this RouteHandlerBuilder builder)
    {
        return builder.AddEndpointFilter<ValidationFilter<T>>();
    }
}
