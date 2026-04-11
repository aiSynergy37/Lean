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

using NodaTime;
using NUnit.Framework;
using QuantConnect.Data;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Interfaces;
using QuantConnect.Lean.Engine.HistoricalData;
using QuantConnect.Research;
using QuantConnect.Securities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QuantConnect.Tests.Research
{
    [TestFixture]
    public class QuantBookOptionHistoryTests
    {
        [Test]
        public void OptionHistoryRespectsExpirationFilterPerDate()
        {
            var qb = new QuantBook();
            var historyProvider = new TestHistoryProvider(qb.HistoryProvider);
            qb.SetHistoryProvider(historyProvider);

            var spx = qb.AddIndex("SPX", Resolution.Daily);
            var spxw = qb.AddIndexOption(spx.Symbol, "SPXW", Resolution.Daily);
            spxw.SetFilter(universe => universe.WeeklysOnly().CallsOnly().Expiration(1, 2).Strikes(-1, 1));

            var start = new DateTime(2021, 1, 4);
            var end = new DateTime(2021, 1, 7);
            var history = qb.OptionHistory(spxw.Symbol, start, end, Resolution.Daily, fillForward: false, extendedMarketHours: false).ToList();

            var optionData = history
                .SelectMany(slice => slice.AllData.Where(data => data.Symbol.SecurityType == SecurityType.Option))
                .ToList();

            Assert.IsNotEmpty(optionData);

            foreach (var data in optionData)
            {
                var daysToExpiry = (data.Symbol.ID.Date.Date - data.EndTime.Date).TotalDays;
                Assert.That(daysToExpiry, Is.GreaterThanOrEqualTo(1).And.LessThanOrEqualTo(2),
                    $"{data.Symbol}: end time {data.EndTime:yyyy-MM-dd} has {daysToExpiry} DTE");
            }

            var optionRequests = historyProvider.HistoryRequests
                .Where(request => request.DataType != typeof(OptionUniverse)
                                  && request.Symbol.SecurityType == SecurityType.Option
                                  && !request.Symbol.IsCanonical())
                .ToList();

            Assert.IsNotEmpty(optionRequests);
            foreach (var request in optionRequests)
            {
                Assert.That(request.EndTimeLocal - request.StartTimeLocal, Is.LessThanOrEqualTo(TimeSpan.FromDays(1)),
                    $"Option request over-fetches range: {request.Symbol} {request.StartTimeLocal:yyyy-MM-dd HH:mm} -> {request.EndTimeLocal:yyyy-MM-dd HH:mm}");
            }
        }

        private class TestHistoryProvider : HistoryProviderBase
        {
            private readonly IHistoryProvider _provider;

            public List<HistoryRequest> HistoryRequests { get; } = new();

            public override int DataPointCount => _provider.DataPointCount;

            public TestHistoryProvider(IHistoryProvider provider)
            {
                _provider = provider;
            }

            public override void Initialize(HistoryProviderInitializeParameters parameters)
            {
            }

            public override IEnumerable<Slice> GetHistory(IEnumerable<HistoryRequest> requests, DateTimeZone sliceTimeZone)
            {
                requests = requests.ToList();
                HistoryRequests.AddRange(requests);
                return _provider.GetHistory(requests, sliceTimeZone);
            }
        }
    }
}
