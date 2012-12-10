﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Simple.OData.Client
{
    public partial class FilterExpression
    {
        internal string Format(ODataClientWithCommand client, Table table)
        {
            return this.Format(new ExpressionContext() { Client = client, Table = table });
        }

        internal string Format(ExpressionContext context)
        {
            if (_operator == ExpressionOperator.None)
            {
                return _reference != null ?
                    FormatReference(context) : _function != null ?
                    FormatFunction(context) :
                    FormatValue(context);
            }
            else if (_operator == ExpressionOperator.NOT)
            {
                var left = FormatExpression(_left, context);
                var op = FormatOperator(context);
                if (NeedsGrouping(_left))
                    return string.Format("{0}({1})", op, left);
                else
                    return string.Format("{0} {1}", op, left);
            }
            else
            {
                var left = FormatExpression(_left, context);
                var right = FormatExpression(_right, context);
                var op = FormatOperator(context);
                if (NeedsGrouping(_left))
                    return string.Format("({0}) {1} {2}", left, op, right);
                else if (NeedsGrouping(_right))
                    return string.Format("{0} {1} ({2})", left, op, right);
                else
                    return string.Format("{0} {1} {2}", left, op, right);
            }
        }

        private static string FormatExpression(FilterExpression expr, ExpressionContext context)
        {
            if (ReferenceEquals(expr, null))
            {
                return "null";
            }
            else
            {
                return expr.Format(context);
            }
        }

        private string FormatReference(ExpressionContext context)
        {
            var elementNames = new List<string>(_reference.Split('.'));
            var pathNames = BuildReferencePath(new List<string>(), null, elementNames, context);
            return string.Join("/", pathNames.Skip(1).ToList());
        }

        private string FormatFunction(ExpressionContext context)
        {
            ExpressionFunction.FunctionMapping mapping;
            if (ExpressionFunction.SupportedFunctions.TryGetValue(new ExpressionFunction.FunctionCall(_function.FunctionName, _function.Arguments.Count()), out mapping))
            {
                var mappedFunction = mapping.FunctionMapper(_function.FunctionName, _function.Target.Format(context), _function.Arguments)._function;
                return string.Format("{0}({1})", mappedFunction.FunctionName,
                    string.Join(",", (IEnumerable<object>)mappedFunction.Arguments.Select(x => FormatExpression(x, context))));
            }
            else
            {
                throw new NotSupportedException(string.Format("The function {0} is not supported or called with wrong number of arguments", _function.FunctionName));
            }
        }

        private string FormatValue(ExpressionContext context)
        {
            return _value == null ?
                "null" : _value is FilterExpression ?
                (_value as FilterExpression).Format(context) :
                _value is string ?
                string.Format("'{0}'", _value) :
                _value is bool ?
                _value.ToString().ToLower() :
                _value.ToString();
        }

        private string FormatOperator(ExpressionContext context)
        {
            switch (_operator)
            {
                case ExpressionOperator.AND:
                    return "and";
                case ExpressionOperator.OR:
                    return "or";
                case ExpressionOperator.NOT:
                    return "not";
                case ExpressionOperator.EQ:
                    return "eq";
                case ExpressionOperator.NE:
                    return "ne";
                case ExpressionOperator.GT:
                    return "gt";
                case ExpressionOperator.GE:
                    return "ge";
                case ExpressionOperator.LT:
                    return "lt";
                case ExpressionOperator.LE:
                    return "le";
                case ExpressionOperator.ADD:
                    return "add";
                case ExpressionOperator.SUB:
                    return "sub";
                case ExpressionOperator.MUL:
                    return "mul";
                case ExpressionOperator.DIV:
                    return "div";
                case ExpressionOperator.MOD:
                    return "mod";
                default:
                    return null;
            }
        }

        private IEnumerable<string> BuildReferencePath(List<string> pathNames, Table table, List<string> elementNames, ExpressionContext context)
        {
            if (!elementNames.Any())
            {
                return pathNames;
            }

            var objectName = elementNames.First();
            if (!pathNames.Any() && context != null && context.IsSet)
            {
                pathNames.Add(context.Table.ActualName);
                return BuildReferencePath(pathNames, context.Table, elementNames, context);
            }
            else if (table != null)
            {
                if (table.HasColumn(objectName))
                {
                    pathNames.Add(table.FindColumn(objectName).ActualName);
                    return BuildReferencePath(pathNames, null, elementNames.Skip(1).ToList(), context);
                }
                else if (table.HasAssociation(objectName))
                {
                    var association = table.FindAssociation(objectName);
                    pathNames.Add(association.ActualName);
                    return BuildReferencePath(pathNames, context.Client.Schema.FindTable(association.ReferenceTableName), elementNames.Skip(1).ToList(), context);
                }
                else
                {
                    ExpressionFunction.FunctionMapping mapping;
                    if (ExpressionFunction.SupportedFunctions.TryGetValue(new ExpressionFunction.FunctionCall(objectName, 0), out mapping))
                    {
                        string targetName = _parent.Format(context);
                        var mappedFunction = mapping.FunctionMapper(objectName, targetName, null)._function;
                        var formattedFunction = string.Format("{0}({1})", mappedFunction.FunctionName, targetName);
                        pathNames.Add(formattedFunction);
                        return BuildReferencePath(pathNames, null, elementNames.Skip(1).ToList(), context);
                    }
                    else
                    {
                        throw new UnresolvableObjectException(objectName, string.Format("Invalid referenced object {0}", objectName));
                    }
                }
            }
            else if (ExpressionFunction.SupportedFunctions.ContainsKey(new ExpressionFunction.FunctionCall(elementNames.First(), 0)))
            {
                string targetName = _parent.Format(context);
                var mapping = ExpressionFunction.SupportedFunctions[new ExpressionFunction.FunctionCall(elementNames.First(), 0)];
                var mappedFunction = mapping.FunctionMapper(objectName, targetName, null)._function;
                var formattedFunction = string.Format("{0}({1})", mappedFunction.FunctionName, targetName);
                pathNames.Add(formattedFunction);
                return BuildReferencePath(pathNames, null, elementNames.Skip(1).ToList(), context);
            }
            else
            {
                pathNames.AddRange(elementNames);
                return BuildReferencePath(pathNames, null, new List<string>(), context);
            }
        }

        private int GetPrecedence(ExpressionOperator op)
        {
            switch (op)
            {
                case ExpressionOperator.NOT:
                    return 1;
                case ExpressionOperator.MOD:
                case ExpressionOperator.MUL:
                case ExpressionOperator.DIV:
                    return 2;
                case ExpressionOperator.ADD:
                case ExpressionOperator.SUB:
                    return 3;
                case ExpressionOperator.GT:
                case ExpressionOperator.GE:
                case ExpressionOperator.LT:
                case ExpressionOperator.LE:
                    return 4;
                case ExpressionOperator.EQ:
                case ExpressionOperator.NE:
                    return 5;
                case ExpressionOperator.AND:
                    return 6;
                case ExpressionOperator.OR:
                    return 7;
                default:
                    return 0;
            }
        }

        private bool NeedsGrouping(FilterExpression expr)
        {
            if (_operator == ExpressionOperator.None)
                return false;
            if (ReferenceEquals(expr, null))
                return false;
            if (expr._operator == ExpressionOperator.None)
                return false;

            int outerPrecedence = GetPrecedence(_operator);
            int innerPrecedence = GetPrecedence(expr._operator);
            return outerPrecedence < innerPrecedence;
        }
    }
}
