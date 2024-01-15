using System.Collections.Generic;
using System.Linq;

using Adletec.Sonic.Execution;
using Adletec.Sonic.Operations;

namespace Adletec.Sonic.Parsing
{
    public class Optimizer
    {
        private readonly IExecutor executor;

        public Optimizer(IExecutor executor)
        {
            this.executor = executor;
        }

        public Operation OptimizeNew(
            Operation operation,
            IFunctionRegistry functionRegistry,
            IConstantRegistry constantRegistry)
        {
            switch (operation)
            {
                case Multiplication m:
                    FloatingPointConstant multiplier = new FloatingPointConstant(1);
                    var optimizedOperation = OptimizeMultiplication(
                        ref multiplier,
                        m,
                        functionRegistry,
                        constantRegistry);
                    if (multiplier.Value != 1)
                    {
                        m.Argument1 = multiplier;
                        m.Argument2 = optimizedOperation;
                    }
                    break;
            }

            return Optimize(operation, functionRegistry, constantRegistry);
        }

        private Operation OptimizeMultiplication(
            ref FloatingPointConstant multiplier,
            Multiplication multiplication,
            IFunctionRegistry functionRegistry,
            IConstantRegistry constantRegistry)
        {
            if (TryGetFloatingPointConstant(multiplication.Argument1, out var constant))
            {
                multiplier = new FloatingPointConstant(multiplier.Value * constant.Value);
                if (multiplication.Argument2 is Multiplication arg2)
                    return OptimizeMultiplication(ref multiplier, arg2, functionRegistry, constantRegistry);
                return OptimizeNew(multiplication.Argument2, functionRegistry, constantRegistry);
            }
            if (TryGetFloatingPointConstant(multiplication.Argument2, out constant))
            {
                multiplier = new FloatingPointConstant(multiplier.Value * constant.Value);
                if (multiplication.Argument1 is Multiplication arg1)
                    return OptimizeMultiplication(ref multiplier, arg1, functionRegistry, constantRegistry);
                return OptimizeNew(multiplication.Argument1, functionRegistry, constantRegistry);
            }

            return multiplication;
        }

        private bool TryGetFloatingPointConstant(Operation operation, out FloatingPointConstant constant)
        {
            if ((constant = operation as FloatingPointConstant) != null)
                return true;

            if (operation is IntegerConstant ic)
            {
                constant = new FloatingPointConstant(ic.Value);
                return true;
            }

            return false;
        }

        public Operation Optimize(
            Operation operation,
            IFunctionRegistry functionRegistry,
            IConstantRegistry constantRegistry)
        {
            if (operation.GetType() == typeof(Addition))
            {
                var addition = (Addition)operation;
                addition.Argument1 = Optimize(addition.Argument1, functionRegistry, constantRegistry);
                addition.Argument2 = Optimize(addition.Argument2, functionRegistry, constantRegistry);
                if (addition.Argument1.DependsOnVariables == false && addition.Argument2.DependsOnVariables == false)
                {
                    addition.DependsOnVariables = false;
                }
                if (addition.Argument1.IsIdempotent && addition.Argument2.IsIdempotent)
                {
                    addition.IsIdempotent = true;
                }
            }
            else if (operation.GetType() == typeof(Subtraction))
            {
                var subtraction = (Subtraction)operation;
                subtraction.Argument1 = Optimize(subtraction.Argument1, functionRegistry, constantRegistry);
                subtraction.Argument2 = Optimize(subtraction.Argument2, functionRegistry, constantRegistry);
                if (subtraction.Argument1.DependsOnVariables == false
                    && subtraction.Argument2.DependsOnVariables == false)
                {
                    subtraction.DependsOnVariables = false;
                }
                if (subtraction.Argument1.IsIdempotent && subtraction.Argument2.IsIdempotent)
                {
                    subtraction.IsIdempotent = true;
                }
            }
            else if (operation.GetType() == typeof(Multiplication))
            {
                var multiplication = (Multiplication)operation;
                multiplication.Argument1 = Optimize(multiplication.Argument1, functionRegistry, constantRegistry);
                multiplication.Argument2 = Optimize(multiplication.Argument2, functionRegistry, constantRegistry);

                if (IsZero(multiplication.Argument1) || IsZero(multiplication.Argument2))
                {
                    return new FloatingPointConstant(0.0);
                }

                if (multiplication.Argument1.DependsOnVariables == false
                    && multiplication.Argument2.DependsOnVariables == false)
                {
                    multiplication.DependsOnVariables = false;
                }
                if (multiplication.Argument1.IsIdempotent && multiplication.Argument2.IsIdempotent)
                {
                    multiplication.IsIdempotent = true;
                }
            }
            else if (operation.GetType() == typeof(Division))
            {
                var division = (Division)operation;
                division.Dividend = Optimize(division.Dividend, functionRegistry, constantRegistry);
                division.Divisor = Optimize(division.Divisor, functionRegistry, constantRegistry);
                if (IsZero(division.Dividend))
                {
                    return new FloatingPointConstant(0.0);
                }
                if (division.Dividend.DependsOnVariables == false && division.Divisor.DependsOnVariables == false)
                {
                    division.DependsOnVariables = false;
                }
                if (division.Dividend.IsIdempotent && division.Divisor.IsIdempotent)
                {
                    division.IsIdempotent = true;
                }
            }
            else if (operation.GetType() == typeof(Exponentiation))
            {
                var exponentiation = (Exponentiation)operation;
                exponentiation.Base = Optimize(exponentiation.Base, functionRegistry, constantRegistry);
                exponentiation.Exponent = Optimize(exponentiation.Exponent, functionRegistry, constantRegistry);

                if (IsZero(exponentiation.Exponent))
                {
                    return new FloatingPointConstant(1.0);
                }

                if (IsZero(exponentiation.Base))
                {
                    return new FloatingPointConstant(0.0);
                }

                if (exponentiation.Base.DependsOnVariables == false
                    && exponentiation.Exponent.DependsOnVariables == false)
                {
                    exponentiation.DependsOnVariables = false;
                }
                if (exponentiation.Base.IsIdempotent && exponentiation.Exponent.IsIdempotent)
                {
                    exponentiation.IsIdempotent = true;
                }
            }
            else if (operation.GetType() == typeof(Function))
            {
                var function = (Function)operation;
                IList<Operation> arguments = function.Arguments
                    .Select(a => Optimize(a, functionRegistry, constantRegistry))
                    .ToList();
                function.Arguments = arguments;
                function.IsIdempotent = functionRegistry.GetFunctionInfo(function.FunctionName).IsIdempotent;
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (!function.DependsOnVariables && arguments[i].DependsOnVariables)
                    {
                        function.DependsOnVariables = true;
                    }
                    if (function.IsIdempotent && !arguments[i].IsIdempotent)
                    {
                        function.IsIdempotent = false;
                    }
                    if (function.DependsOnVariables && !function.IsIdempotent)
                    {
                        break;
                    }
                }
                function.DependsOnVariables = arguments.Any(a => a.DependsOnVariables);
            }

            if (!operation.DependsOnVariables
                && operation.IsIdempotent
                && operation.GetType() != typeof(IntegerConstant)
                && operation.GetType() != typeof(FloatingPointConstant))
            {
                double result = executor.Execute(operation, functionRegistry, constantRegistry);
                return new FloatingPointConstant(result);
            }


            return operation;
        }


        private bool IsZero(Operation operation)
        {
            if (operation.GetType() == typeof(FloatingPointConstant))
            {
                return ((FloatingPointConstant)operation).Value == 0.0;
            }

            if (operation.GetType() == typeof(IntegerConstant))
            {
                return ((IntegerConstant)operation).Value == 0;
            }

            return false;
        }
    }
}