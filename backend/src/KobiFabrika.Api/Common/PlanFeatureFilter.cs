using KobiFabrika.Domain.Billing;
using KobiFabrika.Infrastructure.Billing;

namespace KobiFabrika.Api.Common;

/// <summary>
/// Pakete bağlı özellik kapısı (karar 2026-10-17): sadece yazma uçlarına takılır; okuma açık kalır, kapalı özellik
/// salt okunur görünür. Kapalıysa 403 <c>plan_feature</c> ("Kampanyalar Kalfa paketiyle açılır…").
/// </summary>
public static class PlanFeatureFilter
{
    public static RouteHandlerBuilder RequireFeature(this RouteHandlerBuilder builder, PlanFeatures feature) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var plans = context.HttpContext.RequestServices.GetRequiredService<IPlanAccess>();
            if (await plans.FeatureBlockedMessageAsync(feature, context.HttpContext.RequestAborted) is { } message)
            {
                return Problems.PlanFeature(message);
            }

            return await next(context);
        }).ProducesProblem(StatusCodes.Status403Forbidden);
}
