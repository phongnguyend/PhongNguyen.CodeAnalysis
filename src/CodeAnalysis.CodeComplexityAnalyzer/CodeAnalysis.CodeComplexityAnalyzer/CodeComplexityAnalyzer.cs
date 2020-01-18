using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodeAnalysis.CodeComplexityAnalyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class CodeComplexityAnalyzer : DiagnosticAnalyzer
    {
        public const string UnusedLocalVariableRule = "PNCC001";
        public const string HighNumberOfParametersRule = "PNCC002";
        public const string DeepNestedBlocksRule = "PNCC003";
        public const string AbuseStringCompareRule = "PNCC004";
        public const string ComplexIfConditionRule = "PNCC005";

        private static Dictionary<string, DiagnosticDescriptor> _rules;
        public static Dictionary<string, DiagnosticDescriptor> Rules
        {
            get
            {
                if (_rules == null)
                {
                    _rules = new Dictionary<string, DiagnosticDescriptor>
                {
                    {UnusedLocalVariableRule, new DiagnosticDescriptor(UnusedLocalVariableRule, "Unused Local Variable.", "{0} is unused.", "Optimazation", DiagnosticSeverity.Warning, isEnabledByDefault: true, description: "Unused Local Variable.") },
                    {HighNumberOfParametersRule, new DiagnosticDescriptor(HighNumberOfParametersRule, "Number of Parameters.", "{0} has too many parameters ({1} parameters).", "Optimazation", DiagnosticSeverity.Warning, isEnabledByDefault: true, description: "Number of Parameters.") },
                    {DeepNestedBlocksRule, new DiagnosticDescriptor(DeepNestedBlocksRule, "Deep Nested Blocks.", "{0} has deep nested blocks ({1} levels).", "Optimazation", DiagnosticSeverity.Warning, isEnabledByDefault: true, description: "Deep Nested Blocks.") },
                    {AbuseStringCompareRule, new DiagnosticDescriptor(AbuseStringCompareRule, "Use string.Equals instead of string.Compare.", "Use string.Equals instead of string.Compare.", "CleanCode", DiagnosticSeverity.Warning, isEnabledByDefault: true, description: "Use string.Equals instead of string.Compare.") },
                    {ComplexIfConditionRule, new DiagnosticDescriptor(ComplexIfConditionRule, "Complex If Condition.", "Complex If Condition ({0}).", "CleanCode", DiagnosticSeverity.Warning, isEnabledByDefault: true, description: "Complex If Condition.") }
                };
                }
                return _rules;
            }
        }

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rules.Values.ToArray()); } }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(AnalizeLocalVariables, SyntaxKind.MethodDeclaration);
            context.RegisterSyntaxNodeAction(AnalizeMethodParameters, SyntaxKind.MethodDeclaration);
            context.RegisterSyntaxNodeAction(AnalizeMethodDeepNestedBlocks, SyntaxKind.MethodDeclaration);
            context.RegisterSyntaxNodeAction(AnalizeStringCompareUsage, SyntaxKind.EqualsExpression);
            context.RegisterSyntaxNodeAction(AnalizeComplexIfCondition, SyntaxKind.IfStatement);
        }

        private void AnalizeComplexIfCondition(SyntaxNodeAnalysisContext context)
        {
            var node = context.Node as IfStatementSyntax;

            if (node is null || node.Condition is null)
            {
                return;
            }

            var found = 0;
            TraverseIfCondition(context, node.Condition, ref found);

            if (found > 2)
            {
                context.ReportDiagnostic(Diagnostic.Create(Rules[ComplexIfConditionRule], node.Condition.GetLocation(), found));
            }
        }

        private void TraverseIfCondition(SyntaxNodeAnalysisContext context, SyntaxNode node, ref int found)
        {
            foreach (var child in node.ChildTokens())
            {
                if (child.ValueText == "&&" || child.ValueText == "||")
                {
                    found++;
                }
            }

            foreach (var child in node.ChildNodes())
            {
                TraverseIfCondition(context, child, ref found);
            }
        }

        private void AnalizeStringCompareUsage(SyntaxNodeAnalysisContext context)
        {
            if (context.Node is BinaryExpressionSyntax node && node.Left.ToString().StartsWith("string.Compare(") && node.OperatorToken.ValueText == "==" && node.Right.ToString() == "0")
            {
                context.ReportDiagnostic(Diagnostic.Create(Rules[AbuseStringCompareRule], node.GetLocation(), node.ToString()));
            }
        }

        private void AnalizeLocalVariables(SyntaxNodeAnalysisContext context)
        {

            if (!(context.Node is MethodDeclarationSyntax node) || node.Body == null)
            {
                return;
            }

            var test = context.SemanticModel.AnalyzeDataFlow(node.Body);

            var unused = test.VariablesDeclared.Except(test.ReadInside);
            foreach (var symbol in unused)
            {
                if (symbol is ILocalSymbol)
                {
                    var local = symbol as ILocalSymbol;
                    context.ReportDiagnostic(Diagnostic.Create(Rules[UnusedLocalVariableRule], local.Locations[0], local.ToString()));
                }
            }
        }


        private const int _highNumberOfParemeters = 3;
        private void AnalizeMethodParameters(SyntaxNodeAnalysisContext context)
        {
            if (!(context.Node is MethodDeclarationSyntax node) || node.Body == null)
            {
                return;
            }

            var methodName = node.Identifier;
            var numberOfParas = node.ChildNodes().FirstOrDefault(x => x is ParameterListSyntax)
                ?.ChildNodes().Count(x => x is ParameterSyntax);

            if (numberOfParas.HasValue && numberOfParas.Value > _highNumberOfParemeters)
            {
                context.ReportDiagnostic(Diagnostic.Create(Rules[HighNumberOfParametersRule], methodName.GetLocation(), methodName.ToString(), numberOfParas.Value));
            }
        }

        private const int _deepNestedBlocks = 3;
        private void AnalizeMethodDeepNestedBlocks(SyntaxNodeAnalysisContext context)
        {
            if (!(context.Node is MethodDeclarationSyntax node) || node.Body == null)
            {
                return;
            }

            var depthTracker = new List<int>();
            DrilldownBlocks(node, 0, depthTracker);

            var depth = depthTracker.Any() ? depthTracker.Max() : 0;

            if (depth > _deepNestedBlocks)
            {
                var methodName = node.Identifier;
                context.ReportDiagnostic(Diagnostic.Create(Rules[DeepNestedBlocksRule], methodName.GetLocation(), methodName.ToString(), depth));
            }
        }

        private void DrilldownBlocks(SyntaxNode node, int depth, List<int> depthTracker)
        {
            foreach (var child in node.ChildNodes())
            {
                if (child is BlockSyntax)
                {
                    depthTracker.Add(depth);
                    DrilldownBlocks(child, depth + 1, depthTracker);
                }

                DrilldownBlocks(child, depth, depthTracker);
            }
        }
    }
}
