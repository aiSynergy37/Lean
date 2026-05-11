/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.Collections.Generic;
using Accord.Math;
using Accord.Math.Optimization;
using Accord.Statistics;

namespace QuantConnect.Algorithm.Framework.Portfolio
{
    /// <summary>
    /// Provides an implementation of a portfolio optimizer that maximizes the portfolio Sharpe Ratio.
    /// The interval of weights in optimization method can be changed based on the long-short algorithm.
    /// The default model uses flat risk free rate and weight for an individual security range from -1 to 1.
    /// </summary>
    public class MaximumSharpeRatioPortfolioOptimizer : IPortfolioOptimizer
    {
        private double _lower;
        private double _upper;
        private double _riskFreeRate;

        /// <summary>
        /// Initialize a new instance of <see cref="MaximumSharpeRatioPortfolioOptimizer"/>
        /// </summary>
        /// <param name="lower">Lower constraint</param>
        /// <param name="upper">Upper constraint</param>
        /// <param name="riskFreeRate"></param>
        public MaximumSharpeRatioPortfolioOptimizer(double lower = -1, double upper = 1, double riskFreeRate = 0.0)
        {
            _lower = lower;
            _upper = upper;
            _riskFreeRate = riskFreeRate;
        }

        /// <summary>
        /// Sum of all weight is one: 1^T w = 1 / Σw = 1
        /// </summary>
        /// <param name="size">number of variables</param>
        /// <returns>linear constraint object</returns>
        protected LinearConstraint GetBudgetConstraint(int size)
        {
            return new LinearConstraint(size)
            {
                CombinedAs = Vector.Create(size, 1.0),
                ShouldBe = ConstraintType.EqualTo,
                Value = 1.0
            };
        }

        /// <summary>
        /// Boundary constraints on weights: lw ≤ w ≤ up
        /// </summary>
        /// <param name="size">number of variables</param>
        /// <returns>enumeration of linear constraint objects</returns>
        protected IEnumerable<LinearConstraint> GetBoundaryConditions(int size)
        {
            for (int i = 0; i < size; i++)
            {
                yield return new LinearConstraint(size)
                {
                    VariablesAtIndices = new int[] { i },
                    ShouldBe = ConstraintType.GreaterThanOrEqualTo,
                    Value = _lower
                };
                yield return new LinearConstraint(size)
                {
                    VariablesAtIndices = new int[] { i },
                    ShouldBe = ConstraintType.LesserThanOrEqualTo,
                    Value = _upper
                };
            }
        }

        /// <summary>
        /// Perform portfolio optimization for a provided matrix of historical returns and an array of expected returns
        /// </summary>
        /// <param name="historicalReturns">Matrix of annualized historical returns where each column represents a security and each row returns for the given date/time (size: K x N).</param>
        /// <param name="expectedReturns">Array of double with the portfolio annualized expected returns (size: K x 1).</param>
        /// <param name="covariance">Multi-dimensional array of double with the portfolio covariance of annualized returns (size: K x K).</param>
        /// <returns>Array of double with the portfolio weights (size: K x 1)</returns>
        public double[] Optimize(double[,] historicalReturns, double[] expectedReturns = null, double[,] covariance = null)
        {
            covariance = covariance ?? historicalReturns.Covariance();
            var returns = (expectedReturns ?? historicalReturns.Mean(0)).Subtract(_riskFreeRate);

            var size = covariance.GetLength(0);
            var x0 = Vector.Create(size, 1.0 / size);
            if (size <= 1)
            {
                return x0;
            }

            var initialPortfolioReturn = returns.Dot(x0);
            if (Math.Abs(initialPortfolioReturn) < 1e-12)
            {
                return x0;
            }

            var variableCount = size + 1;
            var scaleIndex = size;
            var initialScale = 1 / initialPortfolioReturn;
            var initialGuess = new double[variableCount];
            for (var i = 0; i < size; i++)
            {
                initialGuess[i] = x0[i] * initialScale;
            }
            initialGuess[scaleIndex] = initialScale;

            var returnConstraint = new double[variableCount];
            for (var i = 0; i < size; i++)
            {
                returnConstraint[i] = returns[i];
            }

            var budgetConstraint = Vector.Create(variableCount, 1.0);
            budgetConstraint[scaleIndex] = -1;

            var constraints = new List<LinearConstraint>
            {
                // Sharpe Maximization under Quadratic Constraints
                // https://quant.stackexchange.com/questions/18521/sharpe-maximization-under-quadratic-constraints
                // Charnes-Cooper substitution: y = t*w, t = 1 / ((µ - r_f)^T w)
                new LinearConstraint(variableCount)
                {
                    CombinedAs = returnConstraint,
                    ShouldBe = ConstraintType.EqualTo,
                    Value = 1
                }
            };

            // 1^T y = t
            constraints.Add(new LinearConstraint(variableCount)
            {
                CombinedAs = budgetConstraint,
                ShouldBe = ConstraintType.EqualTo,
                Value = 0
            });

            // t >= 0
            constraints.Add(new LinearConstraint(variableCount)
            {
                VariablesAtIndices = new[] { scaleIndex },
                ShouldBe = ConstraintType.GreaterThanOrEqualTo,
                Value = 0
            });

            // lower * t <= y_i <= upper * t
            for (var i = 0; i < size; i++)
            {
                var lowerBoundConstraint = new double[variableCount];
                lowerBoundConstraint[i] = 1;
                lowerBoundConstraint[scaleIndex] = -_lower;
                constraints.Add(new LinearConstraint(variableCount)
                {
                    CombinedAs = lowerBoundConstraint,
                    ShouldBe = ConstraintType.GreaterThanOrEqualTo,
                    Value = 0
                });

                var upperBoundConstraint = new double[variableCount];
                upperBoundConstraint[i] = 1;
                upperBoundConstraint[scaleIndex] = -_upper;
                constraints.Add(new LinearConstraint(variableCount)
                {
                    CombinedAs = upperBoundConstraint,
                    ShouldBe = ConstraintType.LesserThanOrEqualTo,
                    Value = 0
                });
            }

            // Setup solver
            var objective = new double[variableCount, variableCount];
            for (var row = 0; row < size; row++)
            {
                for (var column = 0; column < size; column++)
                {
                    objective[row, column] = covariance[row, column];
                }
            }
            objective[scaleIndex, scaleIndex] = 1e-12;

            var optfunc = new QuadraticObjectiveFunction(objective, Vector.Create(variableCount, 0.0));
            var solver = new GoldfarbIdnani(optfunc, constraints);

            // Solve problem
            var success = solver.Minimize(initialGuess);
            if (!success || Math.Abs(solver.Solution[scaleIndex]) < 1e-12)
            {
                return x0;
            }

            return solver.Solution.Get(0, size).Divide(solver.Solution[scaleIndex]);
        }
    }
}
