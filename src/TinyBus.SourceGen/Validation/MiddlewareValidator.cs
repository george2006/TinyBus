using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using TinyBus.SourceGen.Model;

namespace TinyBus.SourceGen.Validation;

internal sealed class MiddlewareValidator
{
    public MiddlewareValidationResult Validate(
        ImmutableArray<MiddlewareAnalysis> middleware,
        CancellationToken cancellationToken)
    {
        var definitions = ImmutableArray.CreateBuilder<MiddlewareDefinition>();
        var issues = ImmutableArray.CreateBuilder<MiddlewareIssue>();

        AddInvalidMiddlewareIssues(middleware, definitions, issues, cancellationToken);
        AddDuplicateOrderIssues(definitions, middleware, issues, cancellationToken);

        var orderedDefinitions = definitions
            .OrderBy(definition => definition.Order)
            .ThenBy(definition => definition.TypeName, StringComparer.Ordinal)
            .ToImmutableArray();

        var validation = new MiddlewareValidationResult(
            orderedDefinitions,
            issues.ToImmutable());

        return validation;
    }

    private static void AddInvalidMiddlewareIssues(
        ImmutableArray<MiddlewareAnalysis> middleware,
        ImmutableArray<MiddlewareDefinition>.Builder definitions,
        ImmutableArray<MiddlewareIssue>.Builder issues,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in middleware)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isValid = candidate.IsConcrete && candidate.ImplementsMiddleware;

            if (!isValid)
            {
                var issue = new MiddlewareIssue(
                    MiddlewareIssueKind.InvalidMiddleware,
                    candidate.TypeName,
                    candidate.Location);
                issues.Add(issue);
                continue;
            }

            var definition = new MiddlewareDefinition(candidate.TypeName, candidate.Order);
            definitions.Add(definition);
        }
    }

    private static void AddDuplicateOrderIssues(
        ImmutableArray<MiddlewareDefinition>.Builder definitions,
        ImmutableArray<MiddlewareAnalysis> middleware,
        ImmutableArray<MiddlewareIssue>.Builder issues,
        CancellationToken cancellationToken)
    {
        var duplicateOrders = definitions
            .GroupBy(definition => definition.Order)
            .Where(group => group.Skip(1).Any())
            .OrderBy(group => group.Key);

        foreach (var duplicateOrder in duplicateOrders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var typeNames = duplicateOrder
                .Select(definition => definition.TypeName)
                .OrderBy(typeName => typeName, StringComparer.Ordinal)
                .ToImmutableArray();
            var details = string.Join(", ", typeNames);
            var order = duplicateOrder.Key.ToString();
            var participants = middleware.Where(candidate =>
                candidate.Order == duplicateOrder.Key);

            foreach (var candidate in participants)
            {
                var issue = new MiddlewareIssue(
                    MiddlewareIssueKind.DuplicateOrder,
                    order,
                    candidate.Location,
                    details);
                issues.Add(issue);
            }
        }
    }
}
