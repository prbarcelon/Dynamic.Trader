using System;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using DynamicData.PLinq;
using ReactiveUI;
using Trader.Client.Infrastucture;
using Trader.Domain.Infrastucture;
using Trader.Domain.Model;
using Trader.Domain.Services;

namespace Trader.Client.Views
{
    public class PagedDataViewer : AbstractNotifyPropertyChanged, IDisposable
    {
        private readonly IDisposable _cleanUp;
        private readonly ReadOnlyObservableCollection<TradeProxy> _data;
        private string _searchText;
        private bool _isPauseWorkaroundEnabled;
        private bool _paused;

        public PagedDataViewer(ITradeService tradeService, ISchedulerProvider schedulerProvider)
        {
            PageParameters
                .WhenAnyValue(x => x.CurrentPage)
                .Skip(1)
                .DistinctUntilChanged()
                .Subscribe(_ =>
                {
                    if (IsPauseWorkaroundEnabled)
                    {
                        Paused = true;  // Pause whenever the current page changes
                    }
                });

            //build observable predicate from search text
            var filter = this.WhenValueChanged(t => t.SearchText)
                .Throttle(TimeSpan.FromMilliseconds(250))
                .Select(BuildFilter);

            //build observable sort comparer
            var sort = SortParameters.WhenValueChanged(t => t.SelectedItem)
                .Select(prop => prop.Comparer)
                .ObserveOn(schedulerProvider.Background);

            var pager = PageParameters.WhenChanged(vm=>vm.PageSize,vm=>vm.CurrentPage, (_,size, pge) => new PageRequest(pge, size))
                .StartWith(new PageRequest(1, 25))
                .DistinctUntilChanged()
                .Sample(TimeSpan.FromMilliseconds(100));
            
            // filter, sort, page and bind to observable collection
            _cleanUp = tradeService.All.Connect()
                .BatchIf(this.WhenValueChanged(x => x.Paused), timeOut: null, scheduler: null)
                .Filter(filter) // apply user filter
                .Transform(trade => new TradeProxy(trade), new ParallelisationOptions(ParallelType.Ordered, 5))
                .Sort(sort, SortOptimisations.ComparesImmutableValuesOnly)
                .Page(pager)
                .ObserveOn(schedulerProvider.MainThread)
                .Do(changes =>
                {
                    PageParameters.Update(changes.Response);
                    if (IsPauseWorkaroundEnabled)
                    {
                        Paused = false; // unpause
                    }
                })
                .Bind(out _data)        // update observable collection bindings
                .DisposeMany()          // dispose when no longer required
                .Subscribe();
        }

        private static Func<Trade, bool> BuildFilter(string searchText)
        {
            if (string.IsNullOrEmpty(searchText)) return trade => true;

            return t => t.CurrencyPair.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                                            t.Customer.Contains(searchText, StringComparison.OrdinalIgnoreCase);
        }

        public string SearchText
        {
            get => _searchText;
            set => SetAndRaise(ref _searchText, value);
        }

        public bool IsPauseWorkaroundEnabled
        {
            get => _isPauseWorkaroundEnabled;
            set => SetAndRaise(ref _isPauseWorkaroundEnabled, value);
        }
        public bool Paused
        {
            get => _paused;
            set => SetAndRaise(ref _paused, value);
        }


        public ReadOnlyObservableCollection<TradeProxy> Data => _data;

        public PageParameterData PageParameters { get;} = new PageParameterData(1,25);

        public SortParameterData SortParameters { get; } = new SortParameterData();

        public void Dispose()
        {
            _cleanUp.Dispose();
        }
    }
}