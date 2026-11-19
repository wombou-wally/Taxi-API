using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Taxi.Api.Infrastructure
{
    // Teaches the OpenAPI document that this API uses a Bearer JWT,
    // and marks only the [Authorize] endpoints as needing it.
    internal sealed class BearerSecurityTransformer : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
    {
        private const string SchemeName = "Bearer";

        // Runs once per document: defines the scheme (this is what gives Scalar its Authorize button).
        public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Log in with POST /api/auth/login, then paste the token here."
            };
            return Task.CompletedTask;
        }

        // Runs once per endpoint: adds the lock only when the endpoint requires authentication.
        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            var metadata = context.Description.ActionDescriptor.EndpointMetadata ?? [];

            bool requiresAuth = metadata.OfType<IAuthorizeData>().Any()
                                && !metadata.OfType<IAllowAnonymous>().Any();
            if (!requiresAuth)
            {
                return Task.CompletedTask;
            }

            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(SchemeName, context.Document)] = []
            });
            return Task.CompletedTask;
        }
    }
}