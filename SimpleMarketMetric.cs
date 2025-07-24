#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators
{
    public class SimpleMarketMetrics : Indicator
    {
		private Series<bool> buySignalSeries;
		private Series<bool> sellSignalSeries;
        public EMA emaFast, emaMedium, emaSlow;
        private MFI mfi;
		private List<string> profitLineTags = new List<string>();
		private List<double> supportLevels = new List<double>();
		private List<double> resistanceLevels = new List<double>();
		private Series<double> smoothedTrueRange;
		private Series<double> smoothedDiPlus;
		private Series<double> smoothedDiMinus;
		private Series<double> adxSeries;
		private int lastTrendSwitch = 0;

		private const int Ema8Index = 0;
		private const int Ema13Index = 1;
		private const int Ema21Index = 2;
		private const int mfiIndex = 3;
		private const int trendUpIndex = 4;
		private const int trendDownIndex = 5;
		
        // User Inputs
        private bool enableBuySellSignals;
        private bool enableMaFilter;
        private int maFilterPeriod;
        private string maFilterType;

        protected override void OnStateChange()
        {
			if (State == State.SetDefaults)
			{
				Description									= @"Enter the description for your new custom Indicator here.";
				Name										= "SimpleMarketMetrics";
				Calculate									= Calculate.OnBarClose;
				ProfitTargetTicks = 20;
				MaxProfitLines = 10;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				//Disable this property if your indicator requires custom values that cumulate with each new market data event. 
				//See Help Guide for additional information.
				IsSuspendedWhileInactive					= true;
				
				EnableBuySellSignals = true;
			    EnableMaFilter = true;
			    MaFilterPeriod = 200;
			    MaFilterType = "EMA";
				
				EnableSupportResistance = true;
				SupportResistanceLookback = 20;
				MaxSupportResistanceLines = 20;

				EnableRealPriceLine = true;
				RealPriceLineColor = Brushes.White;
				RealPriceLineStyle = DashStyleHelper.Dot;
				ShowCloseDots = true;
				CloseDotColor = Brushes.White;
				
				EnableDashboard = true;
				EnableDashboardSignals = true;
				MfiBullishColor = Brushes.Lime;
				MfiBearishColor = Brushes.Red;
				IsOverlay = false;
				AddPlot(Brushes.SteelBlue, "EMA 8");
				AddPlot(Brushes.Orange, "EMA 13");
				AddPlot(Brushes.DarkViolet, "EMA 21");
				AddPlot(Brushes.Gray, "MFI Line");
				AddPlot(Brushes.Transparent, "Trend Up");
				AddPlot(Brushes.Transparent, "Trend Down");
			}
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, 1);
				AddDataSeries(BarsPeriodType.Minute, 2);
				AddDataSeries(BarsPeriodType.Minute, 5);
            }
            else if (State == State.DataLoaded)
            {
				buySignalSeries = new Series<bool>(this);
				sellSignalSeries = new Series<bool>(this);
                // Initialize sub-indicators
                emaFast     = EMA(8);
                emaMedium   = EMA(13);
                emaSlow     = EMA(21);
                mfi         = MFI(10);
				smoothedTrueRange = new Series<double>(this);
				smoothedDiPlus = new Series<double>(this);
				smoothedDiMinus = new Series<double>(this);
				adxSeries = new Series<double>(this);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 21)
				return;
//			if (BarsPeriod.BarsPeriodType != BarsPeriodType.HeikenAshi)
//			{
//			    Draw.TextFixed(this, "haWarning", "⚠️ This indicator is designed for Heikin Ashi bars.", TextPosition.Center, Brushes.Yellow, new SimpleFont(), Brushes.Red, Brushes.Transparent, 10);
//			}
//			else
//			{
//			    RemoveDrawObject("haWarning");
//			}
			Values[Ema8Index][0] = emaFast[0];
			Values[Ema13Index][0] = emaMedium[0];
			Values[Ema21Index][0] = emaSlow[0];
			// Sensitivity
			double chopSensitivity = 1;
			
			// True Range (manual version)
			double trueRange = Math.Max(
			    Math.Max(High[0] - Low[0], Math.Abs(High[0] - Close[1])),
			    Math.Abs(Low[0] - Close[1])
			);
			
			// DI+ and DI- calculations
			double upMove = High[0] - High[1];
			double downMove = Low[1] - Low[0];
			
			double diPlusCalc = (upMove > downMove && upMove > 0) ? upMove : 0;
			double diMinusCalc = (downMove > upMove && downMove > 0) ? downMove : 0;

			// Smoothed values (Wilder-style smoothing)
			double smoothedTR = CurrentBar > 1
			    ? smoothedTrueRange[1] - (smoothedTrueRange[1] / chopSensitivity) + trueRange
			    : trueRange;
			
			double smoothedDIPlus = CurrentBar > 1
			    ? smoothedDiPlus[1] - (smoothedDiPlus[1] / chopSensitivity) + diPlusCalc
			    : diPlusCalc;
			
			double smoothedDIMinus = CurrentBar > 1
			    ? smoothedDiMinus[1] - (smoothedDiMinus[1] / chopSensitivity) + diMinusCalc
			    : diMinusCalc;
			
			// Normalize DI+ and DI- to percentage
			double diPlus = (smoothedTR != 0) ? (smoothedDIPlus / smoothedTR) * 100 : 0;
			double diMinus = (smoothedTR != 0) ? (smoothedDIMinus / smoothedTR) * 100 : 0;
			
			// DX and ADX (Wilder smoothing again)
			double dx = (diPlus + diMinus != 0) ? Math.Abs(diPlus - diMinus) / (diPlus + diMinus) * 100 : 0;
			double adx = CurrentBar > 1
			    ? adxSeries[1] + (dx - adxSeries[1]) / chopSensitivity
			    : dx;
			smoothedTrueRange[0] = smoothedTR;
			smoothedDiPlus[0] = smoothedDIPlus;
			smoothedDiMinus[0] = smoothedDIMinus;
			adxSeries[0] = adx;


			// === Trend Direction Logic (Profit Wave Style) ===
			double atrValue = ATR(8)[0];
			double middle = (High[0] + Low[0]) / 2;
			double upLevel = middle - (1.3 * atrValue);
			double dnLevel = middle + (1.3 * atrValue);
			
			double prevTrendUp = (Values[trendUpIndex].Count > 0) ? Values[trendUpIndex][1] : upLevel;
			double prevTrendDown = (Values[trendDownIndex].Count > 0) ? Values[trendDownIndex][1] : dnLevel;
			
			double trendUp = Close[1] > prevTrendUp ? Math.Max(upLevel, prevTrendUp) : upLevel;
			double trendDown = Close[1] < prevTrendDown ? Math.Min(dnLevel, prevTrendDown) : dnLevel;
			
			int trendSwitch = Close[0] > prevTrendDown ? 1 : Close[0] < prevTrendUp ? -1 : 0;
			
			// Store trend direction as a plot (for now)
			Values[trendUpIndex][0] = trendUp;
			Values[trendDownIndex][0] = trendDown;
			
			// Background coloring by trend
			if (trendSwitch == 1)
			    BackBrush = Brushes.DarkGreen;
			else if (trendSwitch == -1)
			    BackBrush = Brushes.DarkRed;
			else
			    BackBrush = lastTrendSwitch == 1 ? Brushes.DarkGreen : Brushes.DarkRed;
			
			lastTrendSwitch = trendSwitch;
			
			// === Signal Conditions ===
			bool strongBullishCandle = Close[0] > Open[0] && Open[0] == Low[0] && Close[0] > emaFast[0] && Close[0] > emaSlow[0];
			bool strongBearishCandle = Close[0] < Open[0] && Open[0] == High[0] && Close[0] < emaFast[0] && Close[0] < emaSlow[0];
			
			double maFilter = EMA(MaFilterPeriod)[0]; // Default to EMA, expand later
			bool maBuy = !EnableMaFilter || Close[0] > maFilter;
			bool maSell = !EnableMaFilter || Close[0] < maFilter;
			
			bool mfiBuy = MFI(10)[0] > 52;
			bool mfiSell = MFI(10)[0] < 48;
			
			bool canBuy = true;
			bool canSell = true;
			
			if (EnableChopFilter)  // <-- this is your user-defined bool input
			{
			    int diPlusRounded = (int)Math.Floor(diPlus);
			    int diMinusRounded = (int)Math.Floor(diMinus);
			
			    canBuy = diPlusRounded > diMinusRounded && diPlusRounded >= 45;
			    canSell = diMinusRounded > diPlusRounded && diMinusRounded >= 45;
			}
			else
			{
			    canBuy = true;
			    canSell = true;
			}

			
			bool buySignal = EnableBuySellSignals &&
			                 trendSwitch == 1 &&
			                 strongBullishCandle &&
			                 maBuy && canBuy &&
			                 mfiBuy;
			
			bool sellSignal = EnableBuySellSignals &&
			                  trendSwitch == -1 &&
			                  strongBearishCandle &&
			                  maSell && canSell &&
			                  mfiSell;
			buySignalSeries[0] = buySignal;
			sellSignalSeries[0] = sellSignal;

			if (buySignal)
			    Draw.ArrowUp(this, "buy" + CurrentBar, true, 0, Low[0] - TickSize * 4, Brushes.Lime);
			
			if (sellSignal)
			    Draw.ArrowDown(this, "sell" + CurrentBar, true, 0, High[0] + TickSize * 4, Brushes.Red);
			
			// Calculate target price
			double targetPrice = buySignal ? 
				Close[0] + (TickSize * ProfitTargetTicks) : 
				Close[0] - (TickSize * ProfitTargetTicks);
			
			string tag = "target" + CurrentBar;
			Draw.HorizontalLine(this, tag, targetPrice, Brushes.Gold);
			Draw.Text(this, tag + "label", $"Target: {targetPrice:F2}", 0, targetPrice + TickSize * 2, Brushes.Gold);
			
			// Track for cleanup
			profitLineTags.Add(tag);
			ManageProfitLines();

			if (!EnableSupportResistance || CurrentBar < SupportResistanceLookback * 2)
			    return;
			
			// Detect Pivot High
			if (High[SupportResistanceLookback] == MAX(High, SupportResistanceLookback * 2)[SupportResistanceLookback])
			{
			    double res = High[SupportResistanceLookback];
			    if (!resistanceLevels.Contains(res))
			    {
			        Draw.Line(this, "res" + CurrentBar, SupportResistanceLookback, res, 0, res, Brushes.Red);
			        resistanceLevels.Add(res);
			        if (resistanceLevels.Count > MaxSupportResistanceLines)
			            resistanceLevels.RemoveAt(0);
			    }
			}

			// Detect Pivot Low
			if (Low[SupportResistanceLookback] == MIN(Low, SupportResistanceLookback * 2)[SupportResistanceLookback])
			{
			    double sup = Low[SupportResistanceLookback];
			    if (!supportLevels.Contains(sup))
			    {
			        Draw.Line(this, "sup" + CurrentBar, SupportResistanceLookback, sup, 0, sup, Brushes.Green);
			        supportLevels.Add(sup);
			        if (supportLevels.Count > MaxSupportResistanceLines)
			            supportLevels.RemoveAt(0);
			    }
			}

			if (EnableRealPriceLine)
			{
			    string lineTag = "realPriceLine";
			    RemoveDrawObject(lineTag); // Remove old line
			
				Draw.Line(
				    this,
				    lineTag,
				    false,
				    10, Close[0],     // 10 bars ago
				    0, Close[0],       // current bar
				    RealPriceLineColor,
				    RealPriceLineStyle,
				    2 // line width
				);
			}
			
			if (ShowCloseDots)
			{
			    Draw.Dot(
			        this,
			        "closeDot" + CurrentBar,
			        false,
			        0,
			        Close[0],
			        CloseDotColor
			    );
			}

			if (CurrentBar > 50)
			    RemoveDrawObject("closeDot" + (CurrentBar - 50));
			
			if (!EnableDashboard) return;

			double mfiValue = MFI(10)[0];
			Brush mfiColor = mfiValue > 50 ? MfiBullishColor : MfiBearishColor;
			
			PlotBrushes[mfiIndex][0] = mfiColor;  // Uses first plot for color
			
			// Plot value
			Values[mfiIndex][0] = mfiValue;
			
			// Plot crossover dots if enabled
			if (EnableDashboardSignals)
			{
			    if (buySignal)
			        Draw.Dot(this, "dbBuy" + CurrentBar, false, 0, 20, Brushes.Lime);
			
			    if (sellSignal)
			        Draw.Dot(this, "dbSell" + CurrentBar, false, 0, 80, Brushes.Red);
			}
			
			Draw.HorizontalLine(this, "mfi50", 50, Brushes.DimGray);
			Draw.HorizontalLine(this, "mfi20", 20, Brushes.DarkGreen);
			Draw.HorizontalLine(this, "mfi80", 80, Brushes.DarkRed);
			
			if (!EnableDashboard || BarsInProgress > 0)
			    return;
			
			// Sample: Get MFI from each series
			double mfi1 = Closes[1].Count > 10 ? MFI(Closes[1], 10)[0] : 0;
			double mfi2 = Closes[2].Count > 10 ? MFI(Closes[2], 10)[0] : 0;
			double mfi5 = Closes[3].Count > 10 ? MFI(Closes[3], 10)[0] : 0;
			
			// Simple color rules
			Brush mfi1Color = mfi1 > 50 ? Brushes.Lime : Brushes.Red;
			Brush mfi2Color = mfi2 > 50 ? Brushes.Lime : Brushes.Red;
			Brush mfi5Color = mfi5 > 50 ? Brushes.Lime : Brushes.Red;
			
			// Assuming Profit Wave uses EMA(21)
			double emaSlow1 = EMA(Closes[1], 21)[0];
			double emaSlow2 = EMA(Closes[2], 21)[0];
			double emaSlow5 = EMA(Closes[3], 21)[0];
			
			bool trendUp1 = Closes[1][0] > emaSlow1;
			bool trendUp2 = Closes[2][0] > emaSlow2;
			bool trendUp5 = Closes[3][0] > emaSlow5;
			
			string trend1 = trendUp1 ? "Bullish" : "Bearish";
			string trend2 = trendUp2 ? "Bullish" : "Bearish";
			string trend5 = trendUp5 ? "Bullish" : "Bearish";
			
			Brush trendColor1 = trendUp1 ? Brushes.Lime : Brushes.Red;
			Brush trendColor2 = trendUp2 ? Brushes.Lime : Brushes.Red;
			Brush trendColor5 = trendUp5 ? Brushes.Lime : Brushes.Red;
			
			if (BarsInProgress == 0 && State == State.Realtime)
			{
			    string tf1Row = $"1m | MFI: {mfi1:F1} | {trend1}\n";
			    string tf2Row = $"2m | MFI: {mfi2:F1} | {trend2}\n";
			    string tf5Row = $"5m | MFI: {mfi5:F1} | {trend5}\n";
			
			    Draw.TextFixed(this, "matrixHeader", "TF | MFI | Trend", TextPosition.BottomRight);
			    Draw.TextFixed(this, "matrixTF1", tf1Row, TextPosition.BottomRight, mfi1Color, new Gui.Tools.SimpleFont(), Brushes.Transparent, Brushes.Transparent, 15);
			    Draw.TextFixed(this, "matrixTF2", tf2Row, TextPosition.BottomRight, mfi2Color, new Gui.Tools.SimpleFont(), Brushes.Transparent, Brushes.Transparent, 30);
			    Draw.TextFixed(this, "matrixTF3", tf5Row, TextPosition.BottomRight, mfi5Color, new Gui.Tools.SimpleFont(), Brushes.Transparent, Brushes.Transparent, 45);
			}

        }

		private void ManageProfitLines()
		{
		    while (profitLineTags.Count > MaxProfitLines)
		    {
		        string oldestTag = profitLineTags[0];
		        RemoveDrawObject(oldestTag);
		        RemoveDrawObject(oldestTag + "label");
		        profitLineTags.RemoveAt(0);
		    }
		}


		
        #region Properties

        [NinjaScriptProperty]
        [Display(Name="Enable Buy/Sell Signals", Order=1, GroupName="Signal Settings")]
        public bool EnableBuySellSignals
        {
            get { return enableBuySellSignals; }
            set { enableBuySellSignals = value; }
        }

        [NinjaScriptProperty]
        [Display(Name="Enable MA Filter", Order=2, GroupName="Signal Settings")]
        public bool EnableMaFilter
        {
            get { return enableMaFilter; }
            set { enableMaFilter = value; }
        }

        //[NinjaScriptProperty]
        [Range(5, 500), NinjaScriptProperty]
        [Display(Name="MA Filter Period", Order=3, GroupName="Signal Settings")]
        public int MaFilterPeriod
        {
            get { return maFilterPeriod; }
            set { maFilterPeriod = value; }
        }

        [NinjaScriptProperty]
        [Display(Name="MA Filter Type", Order=4, GroupName="Signal Settings")]
        public string MaFilterType
        {
            get { return maFilterType; }
            set { maFilterType = value; }
        }
		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Profit Target (Ticks)", Order = 5, GroupName = "Profit Settings")]
		public int ProfitTargetTicks { get; set; }
		
		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name = "Max Profit Lines", Order = 6, GroupName = "Profit Settings")]
		public int MaxProfitLines { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "Enable S/R", Order = 7, GroupName = "Support/Resistance")]
		public bool EnableSupportResistance { get; set; }
		
		[NinjaScriptProperty]
		[Range(5, 100)]
		[Display(Name = "Pivot Sensitivity", Order = 8, GroupName = "Support/Resistance")]
		public int SupportResistanceLookback { get; set; }
		
		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name = "Max Lines", Order = 9, GroupName = "Support/Resistance")]
		public int MaxSupportResistanceLines { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Real Price Line", Order = 10, GroupName = "Real Price")]
		public bool EnableRealPriceLine { get; set; }
		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Line Color", Order = 11, GroupName = "Real Price")]
		public Brush RealPriceLineColor { get; set; }
		
		[Browsable(false)]
		public string RealPriceLineColorSerializable
		{
		    get { return Serialize.BrushToString(RealPriceLineColor); }
		    set { RealPriceLineColor = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[Display(Name = "Line Style", Order = 12, GroupName = "Real Price")]
		public DashStyleHelper RealPriceLineStyle { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "Show Close Dots", Order = 13, GroupName = "Real Price")]
		public bool ShowCloseDots { get; set; }
		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Dot Color", Order = 14, GroupName = "Real Price")]
		public Brush CloseDotColor { get; set; }
		
		[Browsable(false)]
		public string CloseDotColorSerializable
		{
		    get { return Serialize.BrushToString(CloseDotColor); }
		    set { CloseDotColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Enable Dashboard", Order = 15, GroupName = "Dashboard")]
		public bool EnableDashboard { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Buy/Sell Dots", Order = 16, GroupName = "Dashboard")]
		public bool EnableDashboardSignals { get; set; }
		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Bullish MFI Color", Order = 17, GroupName = "Dashboard")]
		public Brush MfiBullishColor { get; set; }
		
		[Browsable(false)]
		public string MfiBullishColorSerializable
		{
		    get { return Serialize.BrushToString(MfiBullishColor); }
		    set { MfiBullishColor = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Bearish MFI Color", Order = 18, GroupName = "Dashboard")]
		public Brush MfiBearishColor { get; set; }
		
		[Browsable(false)]
		public string MfiBearishColorSerializable
		{
		    get { return Serialize.BrushToString(MfiBearishColor); }
		    set { MfiBearishColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Display(Name="Enable Chop Filter", Order=19, GroupName="Dashboard")]
		public bool EnableChopFilter {get;set;}
		
		[Browsable(false)]
		public Series<bool> BuySignalSeries => buySignalSeries;
		
		[Browsable(false)]
		public Series<bool> SellSignalSeries => sellSignalSeries;

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private SimpleMarketMetrics[] cacheSimpleMarketMetrics;
		public SimpleMarketMetrics SimpleMarketMetrics(bool enableBuySellSignals, bool enableMaFilter, int maFilterPeriod, string maFilterType, int profitTargetTicks, int maxProfitLines, bool enableSupportResistance, int supportResistanceLookback, int maxSupportResistanceLines, bool enableRealPriceLine, Brush realPriceLineColor, DashStyleHelper realPriceLineStyle, bool showCloseDots, Brush closeDotColor, bool enableDashboard, bool enableDashboardSignals, Brush mfiBullishColor, Brush mfiBearishColor, bool enableChopFilter)
		{
			return SimpleMarketMetrics(Input, enableBuySellSignals, enableMaFilter, maFilterPeriod, maFilterType, profitTargetTicks, maxProfitLines, enableSupportResistance, supportResistanceLookback, maxSupportResistanceLines, enableRealPriceLine, realPriceLineColor, realPriceLineStyle, showCloseDots, closeDotColor, enableDashboard, enableDashboardSignals, mfiBullishColor, mfiBearishColor, enableChopFilter);
		}

		public SimpleMarketMetrics SimpleMarketMetrics(ISeries<double> input, bool enableBuySellSignals, bool enableMaFilter, int maFilterPeriod, string maFilterType, int profitTargetTicks, int maxProfitLines, bool enableSupportResistance, int supportResistanceLookback, int maxSupportResistanceLines, bool enableRealPriceLine, Brush realPriceLineColor, DashStyleHelper realPriceLineStyle, bool showCloseDots, Brush closeDotColor, bool enableDashboard, bool enableDashboardSignals, Brush mfiBullishColor, Brush mfiBearishColor, bool enableChopFilter)
		{
			if (cacheSimpleMarketMetrics != null)
				for (int idx = 0; idx < cacheSimpleMarketMetrics.Length; idx++)
					if (cacheSimpleMarketMetrics[idx] != null && cacheSimpleMarketMetrics[idx].EnableBuySellSignals == enableBuySellSignals && cacheSimpleMarketMetrics[idx].EnableMaFilter == enableMaFilter && cacheSimpleMarketMetrics[idx].MaFilterPeriod == maFilterPeriod && cacheSimpleMarketMetrics[idx].MaFilterType == maFilterType && cacheSimpleMarketMetrics[idx].ProfitTargetTicks == profitTargetTicks && cacheSimpleMarketMetrics[idx].MaxProfitLines == maxProfitLines && cacheSimpleMarketMetrics[idx].EnableSupportResistance == enableSupportResistance && cacheSimpleMarketMetrics[idx].SupportResistanceLookback == supportResistanceLookback && cacheSimpleMarketMetrics[idx].MaxSupportResistanceLines == maxSupportResistanceLines && cacheSimpleMarketMetrics[idx].EnableRealPriceLine == enableRealPriceLine && cacheSimpleMarketMetrics[idx].RealPriceLineColor == realPriceLineColor && cacheSimpleMarketMetrics[idx].RealPriceLineStyle == realPriceLineStyle && cacheSimpleMarketMetrics[idx].ShowCloseDots == showCloseDots && cacheSimpleMarketMetrics[idx].CloseDotColor == closeDotColor && cacheSimpleMarketMetrics[idx].EnableDashboard == enableDashboard && cacheSimpleMarketMetrics[idx].EnableDashboardSignals == enableDashboardSignals && cacheSimpleMarketMetrics[idx].MfiBullishColor == mfiBullishColor && cacheSimpleMarketMetrics[idx].MfiBearishColor == mfiBearishColor && cacheSimpleMarketMetrics[idx].EnableChopFilter == enableChopFilter && cacheSimpleMarketMetrics[idx].EqualsInput(input))
						return cacheSimpleMarketMetrics[idx];
			return CacheIndicator<SimpleMarketMetrics>(new SimpleMarketMetrics(){ EnableBuySellSignals = enableBuySellSignals, EnableMaFilter = enableMaFilter, MaFilterPeriod = maFilterPeriod, MaFilterType = maFilterType, ProfitTargetTicks = profitTargetTicks, MaxProfitLines = maxProfitLines, EnableSupportResistance = enableSupportResistance, SupportResistanceLookback = supportResistanceLookback, MaxSupportResistanceLines = maxSupportResistanceLines, EnableRealPriceLine = enableRealPriceLine, RealPriceLineColor = realPriceLineColor, RealPriceLineStyle = realPriceLineStyle, ShowCloseDots = showCloseDots, CloseDotColor = closeDotColor, EnableDashboard = enableDashboard, EnableDashboardSignals = enableDashboardSignals, MfiBullishColor = mfiBullishColor, MfiBearishColor = mfiBearishColor, EnableChopFilter = enableChopFilter }, input, ref cacheSimpleMarketMetrics);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.SimpleMarketMetrics SimpleMarketMetrics(bool enableBuySellSignals, bool enableMaFilter, int maFilterPeriod, string maFilterType, int profitTargetTicks, int maxProfitLines, bool enableSupportResistance, int supportResistanceLookback, int maxSupportResistanceLines, bool enableRealPriceLine, Brush realPriceLineColor, DashStyleHelper realPriceLineStyle, bool showCloseDots, Brush closeDotColor, bool enableDashboard, bool enableDashboardSignals, Brush mfiBullishColor, Brush mfiBearishColor, bool enableChopFilter)
		{
			return indicator.SimpleMarketMetrics(Input, enableBuySellSignals, enableMaFilter, maFilterPeriod, maFilterType, profitTargetTicks, maxProfitLines, enableSupportResistance, supportResistanceLookback, maxSupportResistanceLines, enableRealPriceLine, realPriceLineColor, realPriceLineStyle, showCloseDots, closeDotColor, enableDashboard, enableDashboardSignals, mfiBullishColor, mfiBearishColor, enableChopFilter);
		}

		public Indicators.SimpleMarketMetrics SimpleMarketMetrics(ISeries<double> input , bool enableBuySellSignals, bool enableMaFilter, int maFilterPeriod, string maFilterType, int profitTargetTicks, int maxProfitLines, bool enableSupportResistance, int supportResistanceLookback, int maxSupportResistanceLines, bool enableRealPriceLine, Brush realPriceLineColor, DashStyleHelper realPriceLineStyle, bool showCloseDots, Brush closeDotColor, bool enableDashboard, bool enableDashboardSignals, Brush mfiBullishColor, Brush mfiBearishColor, bool enableChopFilter)
		{
			return indicator.SimpleMarketMetrics(input, enableBuySellSignals, enableMaFilter, maFilterPeriod, maFilterType, profitTargetTicks, maxProfitLines, enableSupportResistance, supportResistanceLookback, maxSupportResistanceLines, enableRealPriceLine, realPriceLineColor, realPriceLineStyle, showCloseDots, closeDotColor, enableDashboard, enableDashboardSignals, mfiBullishColor, mfiBearishColor, enableChopFilter);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.SimpleMarketMetrics SimpleMarketMetrics(bool enableBuySellSignals, bool enableMaFilter, int maFilterPeriod, string maFilterType, int profitTargetTicks, int maxProfitLines, bool enableSupportResistance, int supportResistanceLookback, int maxSupportResistanceLines, bool enableRealPriceLine, Brush realPriceLineColor, DashStyleHelper realPriceLineStyle, bool showCloseDots, Brush closeDotColor, bool enableDashboard, bool enableDashboardSignals, Brush mfiBullishColor, Brush mfiBearishColor, bool enableChopFilter)
		{
			return indicator.SimpleMarketMetrics(Input, enableBuySellSignals, enableMaFilter, maFilterPeriod, maFilterType, profitTargetTicks, maxProfitLines, enableSupportResistance, supportResistanceLookback, maxSupportResistanceLines, enableRealPriceLine, realPriceLineColor, realPriceLineStyle, showCloseDots, closeDotColor, enableDashboard, enableDashboardSignals, mfiBullishColor, mfiBearishColor, enableChopFilter);
		}

		public Indicators.SimpleMarketMetrics SimpleMarketMetrics(ISeries<double> input , bool enableBuySellSignals, bool enableMaFilter, int maFilterPeriod, string maFilterType, int profitTargetTicks, int maxProfitLines, bool enableSupportResistance, int supportResistanceLookback, int maxSupportResistanceLines, bool enableRealPriceLine, Brush realPriceLineColor, DashStyleHelper realPriceLineStyle, bool showCloseDots, Brush closeDotColor, bool enableDashboard, bool enableDashboardSignals, Brush mfiBullishColor, Brush mfiBearishColor, bool enableChopFilter)
		{
			return indicator.SimpleMarketMetrics(input, enableBuySellSignals, enableMaFilter, maFilterPeriod, maFilterType, profitTargetTicks, maxProfitLines, enableSupportResistance, supportResistanceLookback, maxSupportResistanceLines, enableRealPriceLine, realPriceLineColor, realPriceLineStyle, showCloseDots, closeDotColor, enableDashboard, enableDashboardSignals, mfiBullishColor, mfiBearishColor, enableChopFilter);
		}
	}
}

#endregion
