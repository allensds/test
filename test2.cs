using System;
using System.Collections;
using System.Collections.Generic;
using QuickFix;
using QuickFix.FIX44;
using System.Linq;
using System.Collections.Concurrent;
using Reset.FXOptions.Interfaces;
using QuickFix.Fields;
using NLog;

namespace Reset.FXOptions.FIX
{
   public class RBSFIX : BaseFIX
   {
      private struct SymbolData
      {
         public string quoteRequestID;
         public string legCurrency;
         public string premiumCurrency;
         public qfQuoteType quoteType;
      }
      private class QuoteData
      {
         public double totalPrice { get; set; }
         public double totalPremium { get; set; }
         public double hedgeAmt { get; set; }
         public double hedgeRate { get; set; }
         public DateTime hedgeDate { get; set; }
         public SideEnum? hedgeSide { get; set; }
         public double? spotRate { get; set; }
         public IList<ILPQuoteLeg> Legs { get; set; }

         public QuoteData()
         {
            totalPrice = 0;
            totalPremium = 0;
            hedgeAmt = 0;
            hedgeRate = 0;
            hedgeSide = null;
            hedgeDate = default(DateTime);
            Legs = new List<ILPQuoteLeg>();
         }
      }
      Hashtable quoteIDs = new Hashtable();
      private ConcurrentDictionary<string, SymbolData> QuoteRequests;
      // The vb.net function DateTime.DayOfWeek returns an integer between 0-6 (sunday-saturday).
      // If the delivery date falls on the weekend, we need to add 1 / 2 more days depending on whether it falls on a saturday or sunday

      private const string DEALABLE_QUOTE = "1";
      private const string DEFAULT_QUOTE_ID = "*";
      private const string BuySide = "1";
      private const string SellSide = "2";
      private const int UserCancelled = 101;
      private const int NoDeal = 102;
      private const int DealtAway = 103;
      private const int TradeIdea = 104;
      private const int TimedOut = 105;
      private const int BASE_PERCENTAGE = 1;

      private const int QUOTE_PERCENTAGE = 2;
      private const int BASE_PIPS = 3;
      private const int QUOTE_PIPS = 4;

      public RBSFIX() : base(true, false)
      {

         QuoteRequests = new ConcurrentDictionary<string, SymbolData>();

      }

      protected override void QuoteRequest(IStrategy orders, qfQuoteType quoteType, qfQuoteDataType quoteDataType)
      {
         logger.Trace("RBS QuoteRequest ");
         quoteIDs.Add(orders.QuoteRequestId, DEFAULT_QUOTE_ID);
         QuoteRequest msgQuoteRequest = null;
         IList<CustomQuickFixGroup> noLegGroups = new List<CustomQuickFixGroup>();

         try
         {
            ILeg order = orders.Orders[0];
            logger.Trace("RBS QuoteRequest - Symbol: " + order.Symbol + " AccountID: " + order.AccountID + " PriceUnit: " + order.PriceUnit + " Price Currency: " + order.PriceCurrency);
            logger.Trace("RBS QuoteRequest - QuoteReqID: " + orders.QuoteRequestId + " side: " + order.Side + " OptionType: " + order.OptionType + " Quantity: " + order.Qty + " Strike Price: " + order.Strike);
            logger.Trace("RBS QuoteRequest - QuoteRType: " + quoteType + " quoteDataType: " + quoteDataType);


            int[] LegGroupFieldOrder = { 654, 556, 687, 21036, 9136, 201, 55, 54, 9124, 6215, 611, 588, 21020, 21030, 21031, 7914, 7915, 9130, 9132, 9131, 9137, 21042, 21043, 21044 };
            string ccy1 = order.Symbol.Substring(0, 3);
            string ccy2 = order.Symbol.Substring(4, 3);

            msgQuoteRequest = new QuoteRequest(new QuickFix.Fields.QuoteReqID(orders.QuoteRequestId));

            DateTime localMkDate = DateTime.Today;
            string strLocalMkDate = localMkDate.ToString("yyyyMMdd");
            msgQuoteRequest.SetField(new StringField(75, strLocalMkDate));

            // Get the correct AccountID. If it is a valid value, then set the tag in the message.			
            if (!string.IsNullOrEmpty(getAccountID(order.AccountID)))
            {
               msgQuoteRequest.SetField(new StringField(1, getAccountID(order.AccountID)));
            }
            // for reset use indicative
            if (orders.QuoteType.HasValue && orders.QuoteType.Value == QuoteTypes.Indicative)
            {
               msgQuoteRequest.SetField(new IntField(537, (int)qfQuoteType.Indicative));
            }
            else
            {
               msgQuoteRequest.SetField(new IntField(537, (int)qfQuoteType.Tradable));
            }           //Quote type 0=indicative 1=tradable

            msgQuoteRequest.SetField(new IntField(9016, (int)orders.DeltaHedgeType));            //Hedge type 0=none 1=Spot 2=FWD should change to use method parameter!!!!!
            msgQuoteRequest.SetField(new StringField(5830, order.PriceCurrency));            //PremiumCcy            

            //Zero cost RR
            if ((orders.StrategyType == StrategyTypes.RiskReversal) && (orders.Orders.Any(x => x.StrikeType == StrikeTypes.Solve)))
            {
               msgQuoteRequest.SetField(new IntField(9201, 0)); // TotalSolveForPremium - Total premium of the structure for the counterparty requested
                                                                // side when solving for strike Requires one leg with a LegStrike(21020) value of “?”
            }


            //For alpha support only CCY1% and CCY2%
            if (order.PriceUnit == PriceUnitType.Percent)
            {
               if (order.PriceCurrency.ToLower() == ccy1.ToLower())
               {
                  msgQuoteRequest.SetField(new IntField(9207, BASE_PERCENTAGE));
               }
               else if (order.PriceCurrency.ToLower() == ccy2.ToLower())
               {
                  msgQuoteRequest.SetField(new IntField(9207, QUOTE_PERCENTAGE));
               }
               //Pips
            }
            else if (order.PriceUnit == PriceUnitType.Pips)
            {
               if (order.PriceCurrency.ToLower() == ccy1.ToLower())
               {
                  msgQuoteRequest.SetField(new IntField(9207, BASE_PIPS));
               }
               else if (order.PriceCurrency.ToLower() == ccy2.ToLower())
               {
                  msgQuoteRequest.SetField(new IntField(9207, QUOTE_PIPS));
               }
            }
            else
            {
               //Premuim is currently not supported now in RBS              
               RaiseOptionsQuoteReject(orders.Orders[0].Symbol, orders.QuoteRequestId, 0, "RBS - AMT is not supported");
               return;
            }


            //654, 556, 687, 21036, 9136, 201, 55, 54, 9124, 6215, 611, 21020
            int startegy = 1;
            switch (orders.StrategyType)
            {
               case StrategyTypes.Vanilla:
                  startegy = 1;
                  break;
               case StrategyTypes.GenericTwoLeg:
                  startegy = 1;
                  break;
               case StrategyTypes.GenericFourLeg:
                  startegy = 1;
                  break;
               case StrategyTypes.RiskReversal:
                  startegy = 1;
                  break;
               case StrategyTypes.Straddle:
                  startegy = 1;
                  break;
               case StrategyTypes.Strangle:
                  startegy = 1;
                  break;
               default:
                  logger.Trace("RBS reject QuoteRequest: strategy '" + orders.StrategyType + "' not supported");
                  RaiseOptionsQuoteReject(order.Symbol, orders.QuoteRequestId, 0, "strategy '" + orders.StrategyType + "' not supported");
                  return;
            }

            double callStrikeOrDelta = 0, putStrikeOrDelta = 0;
            foreach (var orderLeg in orders.Orders)
            {
               double deltaval;
               if (orderLeg.StrikeType == StrikeTypes.Delta)
               {
                  deltaval = orderLeg.PriceDelta * 100;
               }
               else
               {
                  deltaval = orderLeg.Strike;
               }

               if (orderLeg.OptionType == OptionType.Call)
               {
                  callStrikeOrDelta = deltaval;
               }
               else
               {
                  putStrikeOrDelta = deltaval;
               }
            }

            //number of repeating symbols (currency pairs) in request, always '1'
            foreach (var orderLeg in orders.Orders)
            {
               var noLegGroup = new CustomQuickFixGroup(555, 654, LegGroupFieldOrder);
               noLegGroups.Add(noLegGroup);
               noLegGroup.SetField(new StringField(654, orderLeg.LegId));               // unique leg reference ID 
               noLegGroup.SetField(new StringField(556, ccy1));               //notional currency
               noLegGroup.SetField(new IntField(687, Convert.ToInt32(orderLeg.Qty)));
               noLegGroup.SetField(new IntField(21036, 0));               //LegQTyType 0=Amount  
               noLegGroup.SetField(new IntField(9136, startegy));

               //Put or call indicator, required when 9136 is Vanilla
               if (orderLeg.OptionType == OptionType.Put)
               {
                  noLegGroup.SetField(new IntField(201, 0));
               }
               else
               {
                  noLegGroup.SetField(new IntField(201, 1));
               }

               string pair = ccy1 + ccy2;
               noLegGroup.SetField(new StringField(55, pair));               // Symbol 


               //Zero cost RR
               if ((orders.StrategyType == StrategyTypes.RiskReversal) && (orders.Orders.Any(x => x.StrikeType == StrikeTypes.Solve)))
               {

                  if (orders.SideRequestType == TwoWayType.Sell)
                  {
                     logger.Trace("RBS Zero RR request for Sell is not supported ");
                     RaiseOptionsQuoteReject(orders.Orders[0].Symbol, orders.QuoteRequestId, 0, "RBS - Zero RR Sell is not supported");
                     return;
                  }

                  if (orderLeg.OptionType == OptionType.Call)
                  {
                     if (orders.SideRequestType == TwoWayType.Buy)
                        noLegGroup.SetField(new IntField(54, 1));
                     else if (orders.SideRequestType == TwoWayType.Sell)
                        noLegGroup.SetField(new IntField(54, 2));
                  }
                  else //put leg
                  {
                     if (orders.SideRequestType == TwoWayType.Buy)
                        noLegGroup.SetField(new IntField(54, 2));
                     else if (orders.SideRequestType == TwoWayType.Sell)
                        noLegGroup.SetField(new IntField(54, 1));
                  }
               }
               else
               {
                  if (orderLeg.Side == SideEnum.Buy)
                     noLegGroup.SetField(new IntField(54, 1));               //counter party buys
                  else
                     noLegGroup.SetField(new IntField(54, 2));              //Counterparty Sells               
               }

               switch (orderLeg.CutTime)
               {
                  case CutOff.TK:
                     noLegGroup.SetField(new StringField(9124, "TOK"));               //CutOff  = TOK
                     break;
                  case CutOff.LN:
                     noLegGroup.SetField(new StringField(9124, "LON"));               //CutOff = LON
                     break;
                  default:
                     noLegGroup.SetField(new StringField(9124, "NYK"));               //CutOff = NYK
                     break;
               }
               noLegGroup.SetField(new StringField(6215, "B"));

               string strExpireDate = orderLeg.ExpireDate.ToString("yyyyMMdd");
               noLegGroup.SetField(new LegMaturityDate(strExpireDate));               //611

               switch (orderLeg.StrikeType)
               {
                  case StrikeTypes.Delta:

                     if (startegy == 1)
                     {
                        Double deltaVal = orderLeg.PriceDelta * 100;
                        noLegGroup.SetField(new StringField(21020, deltaVal.ToString() + "S"));
                     }
                     else
                     { //RR and Stangle are different strike values                      
                        noLegGroup.SetField(new StringField(21030, callStrikeOrDelta.ToString() + "S"));
                        noLegGroup.SetField(new StringField(21031, putStrikeOrDelta.ToString() + "S"));
                     }
                     break;
                  case StrikeTypes.Solve:
                     noLegGroup.SetField(new StringField(21020, "?"));
                     break;
                  default:
                     //Double deltaVal = orderLeg.PriceDelta * 100;
                     if (startegy == 1)
                     {
                        noLegGroup.SetField(new StringField(21020, orderLeg.Strike.ToString()));
                     }
                     else //RR and Stangle
                     {
                        noLegGroup.SetField(new StringField(21030, callStrikeOrDelta.ToString())); //!!!
                        noLegGroup.SetField(new StringField(21031, putStrikeOrDelta.ToString()));  //!!!
                     }

                     //set spot reference
                     if (orders.QuoteType.HasValue && orders.QuoteType.Value == QuoteTypes.Indicative && orders.SpotRate.HasValue)
                     {
                        noLegGroup.SetField(new DecimalField(7914, Convert.ToDecimal(orders.SpotRate.Value)));
                        noLegGroup.SetField(new DecimalField(7915, Convert.ToDecimal(orders.SpotRate.Value)));
                     }
                     break;
               }

               if (startegy == 4) break;  // in case RR we break the loop after defining one of the leg           
            }

            foreach (var noLegGroup in noLegGroups)
            {
               msgQuoteRequest.AddGroup(noLegGroup);
            }

            if ((getSessionID() != null))
            {
               Session.SendToTarget(msgQuoteRequest, getSessionID());
            }

            // Store some important data that could be used while raising the interestrate and vol events
            SymbolData sData = new SymbolData();
            sData.quoteRequestID = orders.QuoteRequestId;
            sData.legCurrency = ccy1;
            sData.premiumCurrency = ccy2;

            if (orders.QuoteType.HasValue && orders.QuoteType.Value == QuoteTypes.Indicative)
            {
               sData.quoteType = BaseFIX.qfQuoteType.Indicative;
            }
            else
            {
               sData.quoteType = BaseFIX.qfQuoteType.Tradable;
            }
            QuoteRequests.TryAdd(orders.QuoteRequestId, sData);

         }
         catch (System.Exception ex)
         {
            logger.Error("Failure sending QuoteRequest for clientOrdersID: '" + orders.ClientOrdersID.ToString() + "' quoteReqID: '" + orders.QuoteRequestId + "' " + ex.Message + " " + ex.StackTrace);
         }
         finally
         {
            if ((noLegGroups != null))
            {
               foreach (var noLegGroup in noLegGroups)
               {
                  //if (noLegGroup != null)
                  //noLegGroup.Dispose();
               }
            }
            if ((msgQuoteRequest != null))
            {
               // msgQuoteRequest.Dispose();
            }
         }
      }
      public override void OnMessage(Quote mdMessage, QuickFix.SessionID sessionID)
      {
         IList<CustomQuickFixGroup> LegGroups = new List<CustomQuickFixGroup>();
         IList<CustomQuickFixGroup> LegPricesGroups = new List<CustomQuickFixGroup>();
         CustomQuickFixGroup HedgeGroup = null;
         CustomQuickFixGroup NoHedgePricesGroup = null;
         CustomQuickFixGroup NoTradeDirectionGroup = null;
         //read total premium and price from the group
         QuoteData askQuote = new QuoteData();
         QuoteData bidQuote = new QuoteData();
         QuoteData currQuoteData;
         try
         {
            double midPremium = 0;
            DateTime premiumDeliveryDate = DateTime.Today;
            //string cuttime = null;
            string symbol = "";
            string premiumCurrency = null;
            string hedRefLegIdStr = "";

            string quoteID = mdMessage.GetField(new QuoteID()).getValue();
            logger.Trace("* RBS onMessage Quote, quoteID " + quoteID);
            string quoteReqID = mdMessage.GetField(new QuoteReqID()).getValue();
            logger.Trace("* RBS onMessage Quote, parse Quote request ID " + quoteReqID);
            string quoteType = mdMessage.GetField(new CharField(537)).getValue().ToString();
            logger.Trace("* RBS onMessage Quote, parse Quote Type (1=tradable) " + quoteType);
            if (!quoteIDs.ContainsKey(quoteReqID))
            {
               quoteIDs.Add(quoteReqID, quoteID);
            }
            else
            {
               quoteIDs[quoteReqID] = quoteID;
            }

            int[] NoHedgeGroupOrder = { 55, 9222, 21023 };
            int[] NoHedgePricesGroupOrder = { 9123, 9657, 21013, 9074, 9112 };
            int[] NoTradeDirectionOrder = { 21022, 44, 21026 };
            int[] noLegsOrder = { 654, 556, 687, 55, 54, 9136, 201, 9124, 6215, 611, 588, 21020, 21030, 21031, 612, 21032, 21033, 9130, 9132, 9131, 9137, 21042, 21043, 21044, 21012 };
            int[] noLegPricesOrder = { 21013, 566, 5678, 811, 21034, 21035, 5235, 5191, 9302 };

            //check if the Hashtable contains data for the quoteReqID
            if (QuoteRequests.ContainsKey(quoteReqID))
            {
               SymbolData symbolData = (SymbolData)(QuoteRequests[quoteReqID]);
               // Raise the RFQQuote event only if the quote is dealable

               if ((quoteType == "1" && symbolData.quoteType == qfQuoteType.Tradable) || (quoteType == "0" && symbolData.quoteType == qfQuoteType.Indicative))
               {
                  logger.Trace("* RBS onMessage Quote, Inside tradable flow " + quoteID);

                  try
                  {
                     midPremium = Convert.ToDouble(Math.Abs(mdMessage.GetField(new DecimalField(631)).getValue()));
                     logger.Trace("* RBS onMessage Quote, mid Premium " + midPremium);
                  }
                  catch (System.Exception ex)
                  {
                     logger.Error(ex.Message);
                     midPremium = 0;
                  }

                  premiumCurrency = mdMessage.GetString(5830);
                  logger.Trace("RBS onMessage get premium currency: " + premiumCurrency);

                  try
                  {
                     premiumDeliveryDate = DateTime.ParseExact(mdMessage.GetField(new StringField(5020)).getValue(), "yyyyMMdd", new System.Globalization.CultureInfo("en-US"));
                     logger.Trace("** RBS handle Quote message premiumDeliveryDate: " + premiumDeliveryDate);
                  }
                  catch (System.Exception)
                  {
                     logger.Error("RBS premiumDeliveryDate not exist");
                     premiumDeliveryDate = DateTime.ParseExact("19000101", "yyyyMMdd", new System.Globalization.CultureInfo("en-US"));
                  }



                  int legEntryCount = mdMessage.GetField(new IntField(555)).getValue();
                  logger.Trace("* RBS onMessage Quote, number of legs " + legEntryCount);
                  for (int i = 1, index = 0; i <= legEntryCount; i++, index++)
                  {
                     askQuote.Legs.Add(new LPQuoteLeg());
                     bidQuote.Legs.Add(new LPQuoteLeg());
                     var LegGroup = new CustomQuickFixGroup(555, 654, noLegsOrder);
                     LegGroups.Add(LegGroup);
                     logger.Trace("* RBS onMessage Quote, after create container for group 555 - 654 ");
                     mdMessage.GetGroup(i, LegGroup);
                     logger.Trace("* RBS onMessage Quote, after load leg group ");

                     //get side tag 54 1=Buy 2=Sell
                     string side = LegGroup.GetField(new Side()).getValue().ToString();
                     logger.Trace("* RBS onMessage Quote, get side: " + side);

                     if (side == BuySide)
                     {
                        askQuote.Legs[index].LegSide = SideEnum.Buy;
                        bidQuote.Legs[index].LegSide = SideEnum.Sell;
                     }
                     else
                     {
                        askQuote.Legs[index].LegSide = SideEnum.Sell;
                        bidQuote.Legs[index].LegSide = SideEnum.Buy;
                     }

                     //symbol tag 55
                     symbol = LegGroup.GetField(new StringField(55)).getValue();
                     logger.Trace("* RBS onMessage Quote, get symbol: " + symbol);

                     bidQuote.Legs[index].ExpirationDate = askQuote.Legs[index].ExpirationDate = DateTime.ParseExact(LegGroup.GetField(new StringField(611)).getValue(), "yyyyMMdd", new System.Globalization.CultureInfo("en-US"));
                     logger.Trace("* RBS onMessage Quote, get tenor: " + askQuote.Legs[index].ExpirationDate);

                     bidQuote.Legs[index].LegID = askQuote.Legs[index].LegID = LegGroup.GetInt(654).ToString(); //LegRefID

                     bidQuote.Legs[index].LegQtyCurrency = askQuote.Legs[index].LegQtyCurrency = LegGroup.GetString(556);

                     bidQuote.Legs[index].LegQuantity = askQuote.Legs[index].LegQuantity = Convert.ToDouble(LegGroup.GetDecimal(687));

                     try
                     {
                        bidQuote.Legs[index].LegDeliveryDate = askQuote.Legs[index].LegDeliveryDate = DateTime.ParseExact(LegGroup.GetField(new StringField(588)).getValue(), "yyyyMMdd", new System.Globalization.CultureInfo("en-US"));
                     }
                     catch (Exception)
                     {
                        logger.Error("RBS legSettlDate not exist");
                        bidQuote.Legs[index].LegDeliveryDate = askQuote.Legs[index].LegDeliveryDate = DateTime.ParseExact("19000101", "yyyyMMdd", new System.Globalization.CultureInfo("en-US"));
                     }
                     try
                     {
                        bidQuote.Legs[index].Strike = askQuote.Legs[index].Strike = Convert.ToDouble(Math.Abs(LegGroup.GetField(new DecimalField(21020)).getValue()));
                     }
                     catch (Exception ex)
                     {
                        logger.Error(ex.Message);
                        // if strike is derived it is in taq 612
                        bidQuote.Legs[index].Strike = askQuote.Legs[index].Strike = Convert.ToDouble(Math.Abs(LegGroup.GetField(new DecimalField(612)).getValue()));
                     }

                     //cuttime = LegGroup.GetField(new StringField(9124)).getValue();

                     //number of leg prices should be 2 for two side quote                     
                     int noLegPrices = LegGroup.GetField(new IntField(21012)).getValue();
                     logger.Trace("* RBS onMessage Quote, number of no legs prices " + noLegPrices);
                     currQuoteData = askQuote;
                     for (int j = 1; j <= noLegPrices; j++)
                     {
                        logger.Trace("* RBS onMessage Quote, Inside loop to read leg prices: " + noLegPrices);
                        CustomQuickFixGroup LegPricesGroup = new CustomQuickFixGroup(21012, 21013, noLegPricesOrder);
                        LegPricesGroups.Add(LegPricesGroup);
                        logger.Trace("* RBS onMessage Quote, after create container for group 21012 - 21013 ");

                        LegGroup.GetGroup(j, LegPricesGroup);
                        logger.Trace("* RBS onMessage Quote, Inside loop after load leg prices group: " + i);

                        //leg direction, depends on the side LegDirection (21013) = 1 indicates prices as per counterparty request in tag 54 and LegDirection (21013) = 2 is the opposite
                        char legdirection = LegPricesGroup.GetField(new CharField(21013)).getValue();
                        logger.Trace("* RBS onMessage Quote, get leg direction: " + legdirection);

                        if (legdirection == '2')
                           currQuoteData = bidQuote;

                        try
                        {
                           currQuoteData.Legs[index].Volatility = Convert.ToDouble((LegPricesGroup.GetField(new DecimalField(5678)).getValue()) / 100);
                        }
                        catch (Exception)
                        {
                           currQuoteData.Legs[index].Volatility = 0;
                        }
                        logger.Trace("* RBS onMessage Quote, legvolatility tag 5678: " + currQuoteData.Legs[index].Volatility);

                        if (LegPricesGroup.IsSetField(811))
                        {
                           currQuoteData.Legs[index].Delta = Convert.ToDouble((Math.Abs(LegPricesGroup.GetDecimal(811))) / 100);
                        }
                        if (LegPricesGroup.IsSetField(21034))
                        {
                           currQuoteData.Legs[index].DeltaCall = Convert.ToDouble((Math.Abs(LegPricesGroup.GetDecimal(21034))) / 100);
                        }
                        if (LegPricesGroup.IsSetField(21035))
                        {
                           currQuoteData.Legs[index].DeltaPut = Convert.ToDouble((Math.Abs(LegPricesGroup.GetDecimal(21035))) / 100);
                        }
                        double price = Convert.ToDouble(LegPricesGroup.GetField(new DecimalField(566)).getValue() / 100);
                        currQuoteData.Legs[index].LegPrice = Convert.ToDouble(Math.Abs(price));
                        currQuoteData.totalPrice += price;
                        double premium = Convert.ToDouble(LegPricesGroup.GetField(new DecimalField(9302)).getValue());
                        currQuoteData.Legs[index].LegPremium = Math.Abs(premium);
                        currQuoteData.totalPremium += premium;

                        if (LegPricesGroup.IsSetField(5235))
                        {
                           currQuoteData.spotRate = Convert.ToDouble(LegPricesGroup.GetDecimal(5235));
                        }
                        if (LegPricesGroup.IsSetField(5191))
                        {
                           currQuoteData.Legs[index].ForwardPoints = Convert.ToDouble((LegPricesGroup.GetDecimal(5191) / 5));
                        }
                        logger.Trace("* RBS onMessage Quote, legPremium: " + currQuoteData.Legs[index].LegPremium);
                     }
                  }

                  //Hnadle Delta Hedge details
                  //Dim bidHedgeAmtAs As Double, bidHedgeRate As Double, bidHedgeDate As Date, askHedgeAmt As Double, askHedgeRate As Double, askHedgeDate As Date                    
                  //verify if quote contains delta hedge, read relevant hedge details

                  int noHedgesCount = mdMessage.GetField(new IntField(9221)).getValue();
                  if (noHedgesCount > 0)
                  {
                     //isDeltaHedge = True
                     HedgeGroup = new CustomQuickFixGroup(9221, 9222, NoHedgeGroupOrder);
                     mdMessage.GetGroup(1, HedgeGroup);
                     hedRefLegIdStr = HedgeGroup.GetField(9222);

                     NoHedgePricesGroup = new CustomQuickFixGroup(21023, 9123, NoHedgePricesGroupOrder);
                     int noHedgePrices = HedgeGroup.GetInt(21023);
                     currQuoteData = askQuote;
                     for (int i = 1; i <= noHedgePrices; i++)
                     {
                        HedgeGroup.GetGroup(i, NoHedgePricesGroup);
                        char legdirection = NoHedgePricesGroup.GetField(new CharField(21013)).getValue();
                        logger.Trace("* RBS onMessage Quote, get hedge direction: " + legdirection);

                        // 1 means same as the side of the Quote request which is always a Buy, so 2 is the Bid
                        if (legdirection == '2')
                        {
                           currQuoteData = bidQuote;
                        }

                        currQuoteData.hedgeAmt = Convert.ToDouble(NoHedgePricesGroup.GetField(new DecimalField(9123)));

                        //Positive if RBS pays(LC sells) Counterparty and Negative if Counterparty pays RBS
                        //if (currQuoteData.hedgeAmt >= 0) {
                        //   currQuoteData.hedgeSide = SideEnum.Sell;
                        //}
                        //else {
                        //   currQuoteData.hedgeSide = SideEnum.Buy;
                        //}
                        if (currQuoteData.hedgeAmt >= 0)
                        {
                           currQuoteData.hedgeSide = SideEnum.Buy;
                        }
                        else
                        {
                           currQuoteData.hedgeSide = SideEnum.Sell;
                        }

                        currQuoteData.hedgeRate = Convert.ToDouble(NoHedgePricesGroup.GetField(new DecimalField(9657)));

                        try
                        {
                           currQuoteData.hedgeDate = DateTime.ParseExact(NoHedgePricesGroup.GetField(new StringField(9112)).getValue(), "yyyyMMdd", new System.Globalization.CultureInfo("en-US"));
                        }
                        catch (Exception)
                        {
                           logger.Error("RBS hedgeDate not exist");
                           currQuoteData.hedgeDate = DateTime.ParseExact("19000101", "yyyyMMdd", new System.Globalization.CultureInfo("en-US"));
                        }
                     }
                  }

                  NoTradeDirectionGroup = new CustomQuickFixGroup(21027, 21022, NoTradeDirectionOrder);
                  int noTradeDirections = mdMessage.GetField(new IntField(21027)).getValue();
                  currQuoteData = askQuote;
                  for (int i = 1; i <= noTradeDirections; i++)
                  {
                     mdMessage.GetGroup(i, NoTradeDirectionGroup);
                     char legdirection = NoTradeDirectionGroup.GetField(new CharField(21022)).getValue();
                     logger.Trace("* RBS onMessage Quote, get trade direction: " + legdirection);

                     if (legdirection == '2')
                        currQuoteData = bidQuote;
                     if (NoTradeDirectionGroup.IsSetField(44))
                     {
                        currQuoteData.totalPrice = Convert.ToDouble(Math.Abs(NoTradeDirectionGroup.GetField(new DecimalField(44)).getValue()) / 100);
                     }
                     if (NoTradeDirectionGroup.IsSetField(21026))
                     {
                        currQuoteData.totalPremium = Convert.ToDouble(NoTradeDirectionGroup.GetField(new DecimalField(21026)).getValue());
                     }
                  }

                  var clQuote = new LPQuote(symbol, midPremium, DateTime.Today.ToUniversalTime(), quoteID, quoteReqID, premiumDeliveryDate, "", premiumCurrency,
                          true, null);
                  RFQQuoteEventArgs e = new RFQQuoteEventArgs(clQuote);
                  e.Quote.PriceData[SideTypes.Ask] = new LPQuoteValues(Math.Abs(askQuote.totalPremium), Math.Abs(askQuote.totalPrice),
                     hedRefLegIdStr, askQuote.hedgeAmt, askQuote.hedgeRate, askQuote.hedgeDate, askQuote.totalPremium < 0 ? PayIndicator.Pay : PayIndicator.Receive,
                     askQuote.Legs, askQuote.spotRate);
                  e.Quote.PriceData[SideTypes.Bid] = new LPQuoteValues(Math.Abs(bidQuote.totalPremium), Math.Abs(bidQuote.totalPrice),
                     hedRefLegIdStr, bidQuote.hedgeAmt, bidQuote.hedgeRate, bidQuote.hedgeDate, bidQuote.totalPremium < 0 ? PayIndicator.Pay : PayIndicator.Receive,
                     bidQuote.Legs, bidQuote.spotRate);

                  e.Quote.PriceData[SideTypes.Ask].HedgeSide = askQuote.hedgeSide;
                  e.Quote.PriceData[SideTypes.Bid].HedgeSide = bidQuote.hedgeSide;

                  RaiseRFQQuote(e);
                  // Remove the entry from the table 
                  //QuoteRequests.Remove(quoteReqID)  ??????????? check that all clean and groups are Dispose()
               }
               else
               {
                  logger.Trace("RBS Receive non tradable quote");
               }

               if (symbolData.quoteType == qfQuoteType.Indicative)
               {
                  SendQuoteCancel(symbolData.quoteRequestID, "", "", "", "");
                  RaiseOptionsQuoteReject(string.Empty, quoteReqID, 0, string.Empty);
               }
            }
            else
            {
               logger.Trace("RBS Receive quote ID that is not in cash");
            }


         }
         catch (System.Exception ex)
         {
            logger.Error("Failure processing the RBS Quote message : " + ex.ToString() + " " + ex.Message);
         }
         finally
         {
            if (LegGroups != null)
            {
               // foreach (var leg in LegGroups)
               //  leg.Dispose();
            }

            if (LegPricesGroups != null)
            {
               //   foreach (var leg in LegPricesGroups)
               //leg.Dispose();
            }
            if (HedgeGroup != null)
            {
               //HedgeGroup.Dispose();
            }
         }
      }

      public override void OnMessage(QuoteRequestReject message, QuickFix.SessionID sessionID)
      {
         try
         {
            string quoteReqID = message.Get(new QuoteReqID()).getValue();
            int quoteRejectReason = message.Get(new QuoteRequestRejectReason()).getValue();
            string quoteRejectText = message.Get(new Text()).getValue();

            string symbol = quoteReqID.Substring(0, 7);

            logger.Trace("RBS - OnMessage Reject symbol: " + symbol + " quoteReqID: " + quoteReqID + " quoteRejectReason: " + quoteRejectReason + " quoteRejectText: " + quoteRejectText);

            RaiseOptionsQuoteReject(symbol, quoteReqID, quoteRejectReason, quoteRejectText);

            if (quoteIDs.ContainsKey(quoteReqID))
            {
               quoteIDs.Remove(quoteReqID);
            }
         }
         catch (System.Exception ex)
         {
            logger.Error(ex.Message);
            onStatusChange(qfStatus.qfReject);
            onMessageFromCounterParty(message, sessionID, ex);
         }
      }

      protected override void onMessageFromCounterParty(object Message, QuickFix.SessionID sessionID, System.Exception ex)
      {
         if ((ex != null))
         {
            QuickFix.Message msg = (QuickFix.Message)Message;

            string msgType = msg.Header.GetField(35);
            // U1 and U3 are messages sent back by CreditSuisse when the quoteRequest goes to Dealer Intervention(DI)
            if (msgType.Equals("U1"))
            {
               string messageType = "CreditSuisse Defined Quote Pending Message";
               base.RaiseMessageFromCounterPartyEvent(messageType, msg.ToString(), null);
            }
            else if (msgType.Equals("U3"))
            {
               string messageType = "CreditSuisse Defined Quote State Message";
               base.RaiseMessageFromCounterPartyEvent(messageType, msg.ToString(), null);
            }
         }
         else
         {
            base.onMessageFromCounterParty(Message, sessionID, ex);
         }
      }
      public override void SendQuoteCancel(string quoteReqID, string quoteID, string symbol, string accountID, string tradingSessionID, IStrategy orders = null)
      {
         string lastQuoteID = null;
         QuickFix.FIX44.QuoteCancel quoteCancelMessage = null;
         try
         {
            logger.Trace("RBS sending quoteCancelMessage for quoteReqID: " + quoteReqID + " and quoteID as : " + quoteID);
            if (quoteIDs.ContainsKey(quoteReqID))
            {
               lastQuoteID = quoteIDs[quoteReqID].ToString();
            }
            else
            {
               logger.Trace("RBS - Could Not Find Last QuoteID. :" + quoteReqID + " and quoteID as : " + quoteID);
               return;
            }
            quoteCancelMessage = new QuickFix.FIX44.QuoteCancel();

            quoteCancelMessage.SetField(new StringField(131, quoteReqID));
            quoteCancelMessage.SetField(new StringField(117, lastQuoteID));
            //quoteCancelMessage.SetField(298, "1".ToCharArray()[0]) ' 1 = Quote Cancel            
            if ((getSessionIDfromSenderCompID(accountID) != null))
            {
               if (!(lastQuoteID == DEFAULT_QUOTE_ID))
               {
                  Session.SendToTarget(quoteCancelMessage, getSessionIDfromSenderCompID(accountID));
               }
            }
            else
            {
               throw new System.Exception("RBS - Error. Session not found");
            }

         }
         catch (System.Exception ex)
         {
            logger.Error("RBS - Failure sending quoteCancelMessage for quoteReqID: " + quoteReqID + " and quoteID as : " + quoteID + ex.ToString() + " " + ex.Message);
         }
         finally
         {
            if ((quoteCancelMessage != null))
            {
               //quoteCancelMessage.Dispose();
            }

            if (quoteIDs.ContainsKey(quoteReqID))
            {
               quoteIDs.Remove(quoteReqID);
            }
            // Remove the entry from the table
            SymbolData symb;
            QuoteRequests.TryRemove(quoteReqID, out symb);
         }
      }
      public override void OnMessage(QuoteStatusReport message, QuickFix.SessionID sessionID)
      {
         try
         {
            string quoteRequestID = message.GetField(new StringField(131)).getValue();
            int quoteStatus = message.GetField(new IntField(297)).getValue();

            // 297 = 99 is DI. We want to raise an event for this case so that we can properly display the message in PTP
            // and then send the cancelOrder message
            if (quoteStatus.Equals(99))
            {
               string DIReason = message.GetField(new StringField(58)).getValue();
               RaiseRFQDealerIntervention(quoteRequestID, DIReason);
            }
         }
         catch (System.Exception ex)
         {
            logger.Error("Failure in processing the QuoteStatusReport : " + ex.Message);
         }
      }

      public void OnMessage(QuoteResponse message, QuickFix.SessionID sessionID)
      {
         try
         {
            string quoteRequestID = message.GetField(new StringField(131)).getValue();
            string quoteID = message.GetField(new StringField(117)).getValue();
            if (!quoteIDs.ContainsKey(quoteRequestID))
            {
               quoteIDs.Add(quoteRequestID, quoteID);
            }
            else
            {
               quoteIDs[quoteRequestID] = quoteID;
            }
            int quoteStatus = message.GetField(new IntField(694)).getValue();

            logger.Trace("RBS onMessage Quote, receive QuoteResponse quoteRequestID: " + quoteRequestID + " quoteStatus (101=DI): " + quoteStatus);

            // and then send the cancelOrder message
            if (quoteStatus.Equals(101))
            {
               string DIReason = string.Empty;

               RaiseRFQDealerIntervention(quoteRequestID, DIReason);
            }
         }
         catch (System.Exception ex)
         {
            logger.Error("RBS - Failure in processing the QuoteResponse : " + ex.Message);
         }
      }


      protected override void onMessageQuoteCancel(object erMessage, QuickFix.SessionID sessionID)
      {
         try
         {
            QuickFix.FIX44.QuoteCancel msgQuoteCancel = (QuickFix.FIX44.QuoteCancel)erMessage;
            string quoteReqID = string.Empty;
            string quoteID = string.Empty;
            int quoteCancelType = 0;

            if (msgQuoteCancel.IsSetQuoteReqID())
            {
               quoteReqID = msgQuoteCancel.QuoteReqID.getValue();
            }

            if (msgQuoteCancel.IsSetQuoteID())
            {
               quoteID = msgQuoteCancel.QuoteID.getValue();
            }
            logger.Trace("RBS - quoteCancel message arrives quoteReqID:" + quoteReqID + " quoteID:" + quoteID);
            try
            {
               quoteCancelType = msgQuoteCancel.GetField(new IntField(298)).getValue();
               logger.Trace("RBS - quoteCancelType:" + quoteCancelType);
            }
            catch (System.Exception ex)
            {
               logger.Error(ex.Message);
               quoteCancelType = 0;
            }
            RaiseOptionsQuoteCancel("", quoteID, quoteReqID, 0, "");
         }
         catch (System.Exception ex)
         {
            logger.Error("RBS - Error in processing the quoteCancel message : " + ex.Message);
         }
      }

      // Returns the deliveryDate when the expiryDate is passed-in.
      // Returns the premiumDeliveryDate when the settledDate is passed-in.
      private System.DateTime getDeliveryDate(DateTime referenceDate, string ccy1, string ccy2)
      {
         return DateTime.ParseExact("19000101", "yyyyMMdd", new System.Globalization.CultureInfo("en-US"));
      }

      public override BaseFIX.quoteCancelResponseType RespondsToClientQuoteCancel
      {
         // FIX spec is not clear
         get { return quoteCancelResponseType.Unknown; }
      }
   }
}
