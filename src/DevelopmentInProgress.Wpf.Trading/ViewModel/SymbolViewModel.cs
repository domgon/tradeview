﻿using DevelopmentInProgress.MarketView.Interface.Enums;
using DevelopmentInProgress.MarketView.Interface.Interfaces;
using DevelopmentInProgress.Wpf.Common.Chart;
using DevelopmentInProgress.Wpf.Common.Extensions;
using DevelopmentInProgress.Wpf.Common.Helpers;
using DevelopmentInProgress.Wpf.Common.Model;
using DevelopmentInProgress.Wpf.Common.Services;
using DevelopmentInProgress.Wpf.Common.ViewModel;
using DevelopmentInProgress.Wpf.Trading.Events;
using LiveCharts;
using Prism.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Interface = DevelopmentInProgress.MarketView.Interface.Model;

[assembly: InternalsVisibleTo("DevelopmentInProgress.Wpf.Trading.Test")]
namespace DevelopmentInProgress.Wpf.Trading.ViewModel
{
    public class SymbolViewModel : ExchangeViewModel
    {
        private CancellationTokenSource symbolCancellationTokenSource;
        private Symbol symbol;
        private OrderBook orderBook;
        private ChartValues<TradeBase> tradesChart;
        private List<TradeBase> trades;
        private Exchange exchange;
        private IOrderBookHelper orderBookHelper;
        private object orderBookLock = new object();
        private object tradesLock = new object();
        private bool isLoadingTrades;
        private bool isLoadingOrderBook;
        private bool disposed;

        public SymbolViewModel(Exchange exchange, IWpfExchangeService exchangeService, IChartHelper chartHelper,
            IOrderBookHelper orderBookHelper, Preferences preferences, ILoggerFacade logger)
            : base(exchangeService, logger)
        {
            this.exchange = exchange;
            this.orderBookHelper = orderBookHelper;

            TradeLimit = preferences.TradeLimit;
            TradesDisplayCount = preferences.TradesDisplayCount;
            TradesChartDisplayCount = preferences.TradesChartDisplayCount;

            UseAggregateTrades = preferences.UseAggregateTrades;

            OrderBookLimit = preferences.OrderBookLimit;
            OrderBookDisplayCount = preferences.OrderBookDisplayCount;
            OrderBookChartDisplayCount = preferences.OrderBookChartDisplayCount;
            OrderBookCount = OrderBookChartDisplayCount > OrderBookDisplayCount ? OrderBookChartDisplayCount : OrderBookDisplayCount;

            TimeFormatter = chartHelper.TimeFormatter;
            PriceFormatter = chartHelper.PriceFormatter;

            OnPropertyChanged(string.Empty);
        }

        public event EventHandler<SymbolEventArgs> OnSymbolNotification;

        internal int TradeLimit { get; }
        internal int TradesChartDisplayCount { get; }
        internal int TradesDisplayCount { get; }
        internal bool UseAggregateTrades { get; }
        internal int OrderBookLimit { get; }
        internal int OrderBookChartDisplayCount { get; }
        internal int OrderBookDisplayCount { get; }
        internal int OrderBookCount { get; set; }

        public bool HasSymbol => Symbol != null ? true : false;

        public Func<double, string> TimeFormatter { get; set; }

        public Func<double, string> PriceFormatter { get; set; }

        public bool IsLoadingOrderBook
        {
            get { return isLoadingOrderBook; }
            set
            {
                if (isLoadingOrderBook != value)
                {
                    isLoadingOrderBook = value;
                    OnPropertyChanged("IsLoadingOrderBook");
                }
            }
        }

        public bool IsLoadingTrades
        {
            get { return isLoadingTrades; }
            set
            {
                if (isLoadingTrades != value)
                {
                    isLoadingTrades = value;
                    OnPropertyChanged("IsLoadingTrades");
                }
            }
        }

        public Symbol Symbol
        {
            get { return symbol; }
            set
            {
                if (symbol != value)
                {
                    symbol = value;
                    OnPropertyChanged("Symbol");
                    OnPropertyChanged("HasSymbol");
                }
            }
        }

        public List<TradeBase> Trades
        {
            get { return trades; }
            set
            {
                if (trades != value)
                {
                    trades = value;
                    OnPropertyChanged("Trades");
                }
            }
        }

        public ChartValues<TradeBase> TradesChart
        {
            get { return tradesChart; }
            set
            {
                if (tradesChart != value)
                {
                    tradesChart = value;
                    OnPropertyChanged("TradesChart");
                }
            }
        }

        public OrderBook OrderBook
        {
            get { return orderBook; }
            set
            {
                if (orderBook != value)
                {
                    orderBook = value;
                    OnPropertyChanged("OrderBook");
                }
            }
        }

        public override void Dispose(bool disposing)
        {
            if (disposed)
            {
                return;
            }

            if (disposing)
            {
                if (symbolCancellationTokenSource != null
                    && !symbolCancellationTokenSource.IsCancellationRequested)
                {
                    symbolCancellationTokenSource.Cancel();
                }
            }

            disposed = true;
        }

        public async Task SetSymbol(Symbol symbol)
        {
            try
            {
                if(symbolCancellationTokenSource != null
                    && !symbolCancellationTokenSource.IsCancellationRequested)
                {
                    symbolCancellationTokenSource.Cancel();
                }

                symbolCancellationTokenSource = new CancellationTokenSource();

                Symbol = symbol;
                TradesChart = null;
                Trades = null;
                OrderBook = null;

                var tasks = new List<Task>(new[] { GetOrderBook(), GetTrades()}).ToArray();

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                OnException("SymbolViewModel.SetSymbol", ex);
            }
        }

        private async Task GetOrderBook()
        {
            IsLoadingOrderBook = true;

            try
            {
                var orderBook = await ExchangeService.GetOrderBookAsync(exchange, Symbol.ExchangeSymbol, OrderBookLimit, symbolCancellationTokenSource.Token);

                UpdateOrderBook(orderBook);

                ExchangeService.SubscribeOrderBook(exchange, Symbol.ExchangeSymbol, OrderBookLimit, e => UpdateOrderBook(e.OrderBook), SubscribeOrderBookException, symbolCancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                OnException("SymbolViewModel.GetOrderBook", ex);
            }

            IsLoadingOrderBook = false;
        }

        private async Task GetTrades()
        {
            IsLoadingTrades = true;

            try
            {
                IEnumerable<ITrade> trades;

                if (UseAggregateTrades)
                {
                    trades = await ExchangeService.GetAggregateTradesAsync(exchange, Symbol.Name, TradeLimit, symbolCancellationTokenSource.Token);
                }
                else
                {
                    trades = await ExchangeService.GetTradesAsync(exchange, Symbol.Name, TradeLimit, symbolCancellationTokenSource.Token);
                }

                UpdateTrades(trades);

                if (UseAggregateTrades)
                {
                    ExchangeService.SubscribeAggregateTrades(exchange, Symbol.Name, TradeLimit, e => UpdateTrades(e.Trades), SubscribeTradesException, symbolCancellationTokenSource.Token);
                }
                else
                {
                    ExchangeService.SubscribeTrades(exchange, Symbol.Name, TradeLimit, e => UpdateTrades(e.Trades), SubscribeTradesException, symbolCancellationTokenSource.Token);
                }
            }
            catch (Exception ex)
            {
                OnException("SymbolViewModel.GetTrades", ex);
            }

            IsLoadingTrades = false;
        }

        internal void UpdateOrderBook(Interface.OrderBook orderBook)
        {
            if (!Symbol.ExchangeSymbol.Equals(orderBook.Symbol))
            {
                throw new Exception("Orderbook update for wrong symbol");
            }

            lock (orderBookLock)
            {
                bool firstOrders = false;

                if (OrderBook == null)
                {
                    // First incoming order book create the local order book.
                    firstOrders = true;

                    OrderBook = new OrderBook
                    {
                        Symbol = orderBook.Symbol,
                        BaseSymbol = Symbol.BaseAsset.Symbol,
                        QuoteSymbol = Symbol.QuoteAsset.Symbol
                    };
                }
                else if (OrderBook.LastUpdateId >= orderBook.LastUpdateId)
                {
                    // If the incoming order book is older than the local one ignore it.
                    return;
                }

                OrderBook.LastUpdateId = orderBook.LastUpdateId;

                List<OrderBookPriceLevel> topAsks;
                List<OrderBookPriceLevel> topBids;
                List<OrderBookPriceLevel> chartAsks;
                List<OrderBookPriceLevel> chartBids;
                List<OrderBookPriceLevel> aggregatedAsks;
                List<OrderBookPriceLevel> aggregatedBids;

                orderBookHelper.GetBidsAndAsks(orderBook, symbol.PricePrecision, symbol.QuantityPrecision,
                    OrderBookCount, OrderBookDisplayCount, OrderBookChartDisplayCount,
                    out topAsks, out topBids, out chartAsks, out chartBids, out aggregatedAsks, out aggregatedBids);

                // Create new instances of the top bids and asks, reversing the asks
                OrderBook.TopAsks = topAsks;
                OrderBook.TopBids = topBids;

                if (firstOrders)
                {
                    // Create new instances of the chart bids and asks, reversing the bids.
                    OrderBook.ChartAsks = new ChartValues<OrderBookPriceLevel>(chartAsks);
                    OrderBook.ChartBids = new ChartValues<OrderBookPriceLevel>(chartBids);
                    OrderBook.ChartAggregatedAsks = new ChartValues<OrderBookPriceLevel>(aggregatedAsks);
                    OrderBook.ChartAggregatedBids = new ChartValues<OrderBookPriceLevel>(aggregatedBids);
                }
                else
                {
                    // Update the existing orderbook chart bids and asks, reversing the bids.
                    OrderBook.UpdateChartAsks(chartAsks);
                    OrderBook.UpdateChartBids(chartBids.ToList());
                    OrderBook.UpdateChartAggregateAsks(aggregatedAsks);
                    OrderBook.UpdateChartAggregateBids(aggregatedBids.ToList());
                }
            }
        }

        internal void UpdateTrades(IEnumerable<ITrade> tradesUpdate)
        {
            lock (tradesLock)
            {
                if(Trades == null)
                {
                    List<TradeBase> newTrades;
                    ChartValues<TradeBase> newTradesChart;

                    TradeHelper.SetTrades(tradesUpdate, Symbol.PricePrecision, Symbol.QuantityPrecision, TradesDisplayCount, TradesChartDisplayCount, Logger, out newTrades, out newTradesChart);

                    Trades = newTrades;
                    TradesChart = newTradesChart;
                }
                else
                {
                    List<TradeBase> newTrades;

                    TradeHelper.UpdateTrades(tradesUpdate, Trades, Symbol.PricePrecision, Symbol.QuantityPrecision, TradesDisplayCount, TradesChartDisplayCount, Logger, out newTrades, ref tradesChart);

                    Trades = newTrades;
                }
            }
        }

        private void SubscribeTradesException(Exception exception)
        {
            OnException("SymbolViewModel.GetTrades - ExchangeService.SubscribeTrades", exception);
        }

        private void SubscribeOrderBookException(Exception exception)
        {
            OnException("SymbolViewModel.GetOrderBook - ExchangeService.SubscribeOrderBook", exception);
        }

        private void OnException(string message, Exception exception)
        {
            var onSymbolNotification = OnSymbolNotification;
            onSymbolNotification?.Invoke(this, new SymbolEventArgs { Message = message, Exception = exception });
        }
    }
}