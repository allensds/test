using System;
using System.Collections;
using System.Collections.Generic;
using QuickFix;
using System.Collections.Concurrent;
using Reset.FXOptions.Interfaces;
using QuickFix.Fields;
using QuickFix.FIX43;
using QuickFix.FIX44;
using NLog;

namespace Reset.FXOptions.FIX
{
   public abstract partial class BaseFIX : QuickFix.MessageCracker, QuickFix.IApplication, IDisposable
   {
      protected Logger logger { get; private set; }
      protected object SyncRoot = new object();

      private bool bIsInitiator;
      private bool bResetOnLogon;
      private QuickFix.Transport.SocketInitiator objInitiator;
      private QuickFix.ThreadedSocketAcceptor objAcceptor;

      private SessionSettings objSettings;

      private bool blnStarted;
      protected string targetSubID;
      private string accountID = "";
      protected bool usingPriceImprovement;
      protected string senderSubID = "";

      protected string lastQuoteRequestID = "";
      // This is a temporary configuration for CS till the time they update the PROD systems.
      // The default value will be FALSE = Do not pass midPremium tag in the newOrder message.

      protected bool includeMidPremium = false;
      // Delay in milliseconds we would need to introduce before sending the newOrderMultiLeg message.
      // This delay to specific to Citi only at the moment
      protected int NewOrderDelayMs = 0;
      protected struct subscriptionStruct
      {
         public string Symbol;
         public bool Subscribed;
      }


      protected Hashtable HashSubscriptionData;
      public enum entryType
      {
         bid = 0,
         offer = 1
      }

      public enum cxOrderTypes
      {
         MarketOrder = 0,
         LimitOrder,
         StopOrder,
         ForexMarketOrder,
         ForexLimitOrder
      }

      public enum qfTimeInForce
      {
         Day = 0,
         GoodTillCancel,
         ImmedOrCancel,
         FillOrKill,
         AllOrNone
      }

      public enum qfQuoteType
      {
         Indicative = 0,
         Tradable = 1,
         Dealable = 2,
         Canceled = 3
      }

      public enum qfQuoteDataType
      {
         Volatility = 0,
         InterestRate = 1,
         SpotQuote = 3
      }


      public enum qfExecTypeIndicator
      {
         qfUnknown = -1,
         qfNew = 0,
         qfPartialFill = 1,
         qfFill = 2,
         qfDoneForDay = 3,
         qfCanceled = 4,
         qfReplace = 5,
         qfPendingCancel = 6,
         qfStopped = 7,
         qfRejected = 8,
         qfSuspended = 9,
         qfPendingNew = 10,
         // Is "A" in FIX
         qfCalculated = 11,
         // Is "B" in FIX
         qfExpired = 12,
         // Is "C" in FIX
         qfRestated = 13,
         // Is "D" in FIX
         qfPendingReplace = 14
         // Is "E" in FIX
      }

      public enum qfExecTransactionType
      {
         qfNew = 0,
         qfCancel = 1,
         qfCorrect = 2,
         qfStatus = 3
      }

      public struct qfCXOrderStatus
      {
         public string ClientOrderId;
         public string ExecID;
         public qfExecTransactionType ExecTransType;
         public qfExecTypeIndicator ExecType;
         // same types as ExecTypes
         public qfExecTypeIndicator OrdStatus;
         public string PreExecID;
         public double? Fill;
         //change later to be TotalPrice
         public int RejectReason;
         public string Symbol;
         public string Text;
         //usually 2 days after expiry RBS tag 588 (settle date)
         public System.DateTime? DeliveryDate;
         //2 days RBS tag 5020
         public System.DateTime? PremiumDeliveryDate;

         public string QuoteID;
         public System.DateTime? HedgeDate;
         public double? HedgeRate;
         public double? HedgeAmount;
         public double? TotalPremium;
         public double? PriceDelta;

         public IList<qfCXOrderStatusLeg> legs;
      }

      public class qfCXOrderStatusLeg
      {
         public string LegId;
         public double Quantity;
         public double? LegPremium;
         public double? Delta;
         public double? LegPrice;
         public double? Volatility;
         public double LegStrike;
         public string Maturity;
         public OptionType LegType;
         public DateTime? LegSettleDate;
         public SideEnum LegSide;
         public double? DepoRateBase;
         public double? DepoRateTerm;
      }

    
      public enum qfStatus
      {
         qfUnknown = 0,
         qfLogon,
         qfLogout,
         qfReject
      }

      public enum qfMsgType
      {
         qfUnknown = 0,
         qfQuoteRequest,
         qfQuote,
         qfExecutionReport,
         qfNewOrderMultileg,
         qfLogon,
         qfLogout,
         qfReject
      }

      public struct MassQuoteVols
      {
         public string delta;
         public double bidVol;
         public double askVol;
         public int putCallIndicator;
         public double askStrike;
         public double bidStrike;
      }
      public struct MassQuoteData
      {
         public string tenor;
         public string symbol;
         public Dictionary<string, MassQuoteVols> straddleVols;
         public Dictionary<string, MassQuoteVols> riskReversalVols;
         public Dictionary<string, MassQuoteVols> butterFlyVols;
         public Dictionary<string, MassQuoteVols> vanillaVols;
         public DateTime recDate;
         public DateTime providerExpDate;
      }
      public event HeartbeatEventHandler Heartbeat;
      public delegate void HeartbeatEventHandler(System.DateTime HeartbeatDate);
      public event MessageFromCounterPartyEventHandler MessageFromCounterParty;
      public delegate void MessageFromCounterPartyEventHandler(string MessageType, string RawMessage, System.Exception ex);
      public event MessageToCounterPartyEventHandler MessageToCounterParty;
      public delegate void MessageToCounterPartyEventHandler(string MessageType, string RawMessage, System.Exception ex);
      public event MarketDataEventHandler MarketData;
      public delegate void MarketDataEventHandler(string Symbol, double Bid, double Ask, string Originator, System.DateTime TradeDate, string QuoteID);
      public event RFQQuoteEventHandler RFQQuote;
      public delegate void RFQQuoteEventHandler(object sender, RFQQuoteEventArgs e);
      //public event GeneralFailureEventHandler GeneralFailure;
      public delegate void GeneralFailureEventHandler(object sender, GeneralFailureEventArgs e);

      public class FIXOrder
      {
         public string ClientOrderID;
         public SideEnum side;
         public double Quantity;

         public string AccountID;
         public FIXOrder(string ClientOrderID, SideEnum side, double Quantity, string AccountID)
         {
            this.ClientOrderID = ClientOrderID;
            this.side = side;
            this.Quantity = Quantity;
            this.AccountID = AccountID;
         }
      }

      /// <summary>
      /// FIXOptionOrder data class
      /// </summary>
      /// <remarks>Some of the LPs require data that is provided from the quote, where possible we should use the quote object itself</remarks>
      public class FIXOptionOrder : FIXOrder
      {
         public OptionType optionType;

         public ILPQuote quote;
         public FIXOptionOrder(string clientOrderID, SideEnum side, OptionType optionType, double Quantity, string accountID, ref ILPQuote quote) : base(clientOrderID, side, Quantity, accountID)
         {
            this.optionType = optionType;
            this.quote = quote;
         }
      }

      public class RFQQuoteEventArgs : EventArgs
      {

         public readonly ILPQuote Quote;
         public RFQQuoteEventArgs(ILPQuote Quote)
         {
            this.Quote = Quote;
         }
      }

      public class GeneralFailureEventArgs : EventArgs
      {

         public string quoteRequestId;
        
         public string errorMessage;

         public qfMsgType messageType;

         public void SetMsgType(string msgType)
         {
            switch (msgType)
            {
               case "8":
                  messageType = qfMsgType.qfExecutionReport;
                  break;
               default:
                  messageType = qfMsgType.qfUnknown;
                  break;
            }

         }


      }

      public event VolQuoteEventHandler VolQuote;
      public delegate void VolQuoteEventHandler(string Symbol, double BidPremium, double AskPremium, string QuoteID, string quoteReqID, OptionType putOrCall, double AskStrike, double bidPremium2, string tenor, double bidVol,
      double askVol, string delta, int dealMode, DateTime expDateFromLP);
      public event IntQuoteEventHandler IntQuote;
      public delegate void IntQuoteEventHandler(string Symbol, string QuoteID, string quoteReqID, string tenor, double bidIR, double askIR, string currency, DateTime providerExpDate);
      public event QuoteCancelEventHandler QuoteCancel;
      public delegate void QuoteCancelEventHandler(string Symbol, string QuoteID, string quoteReqID, int quoteCancelType);
      public event OptionsQuoteCancelEventHandler OptionsQuoteCancel;
      public delegate void OptionsQuoteCancelEventHandler(string Symbol, string QuoteID, string quoteReqID, int quoteCancelType, string tenor);
      public event OptionsQuoteRejectEventHandler OptionsQuoteReject;
      public delegate void OptionsQuoteRejectEventHandler(string Symbol, string quoteReqID, int quoteRejectReason, string quoteRejectText);
      public event StatusChangeEventHandler StatusChange;
      public delegate void StatusChangeEventHandler(BaseFIX.qfStatus Status);
      public event OrderStatusUpdateEventHandler OrderStatusUpdate;
      public delegate void OrderStatusUpdateEventHandler(qfCXOrderStatus OrderStatus);
      public event MassQuoteEventHandler MassQuote;
      public delegate void MassQuoteEventHandler(string Symbol, Hashtable massQuoteDataHashTable);
      public event RFQDealerInterventionEventHandler RFQDealerIntervention;
      public delegate void RFQDealerInterventionEventHandler(string quoteReqID, string DIReason);

      /// <summary>
      /// Format of the string timestamp
      /// </summary>
      /// <remarks></remarks>
      protected enum timestampFormat
      {
         /// <summary>
         /// YYYYMMDD
         /// </summary>
         /// <remarks></remarks>
         DateOnly,
         /// <summary>
         /// YYYYMMDDhh:mm:ss
         /// </summary>
         /// <remarks></remarks>
         DateTimeNoDash,
         /// <summary>
         /// YYYYMMDD-hh:mm:ss
         /// </summary>
         /// <remarks></remarks>
         DateTimeDash
      }

      protected static System.DateTime getTimestamp(QuickFix.Message msg, int field, timestampFormat format, System.DateTime defaultDate)
      {
         string sValue = "";
         try
         {
            sValue = msg.GetField(new StringField(field)).getValue();
         }
         catch (Exception)
         {

         }
         return getTimestamp(sValue, format, defaultDate);
      }
      protected static System.DateTime getTimestamp(QuickFix.Group grp, int field, timestampFormat format, System.DateTime defaultDate)
      {
         string sValue = "";
         try
         {
            sValue = grp.GetField(new StringField(field)).getValue();
         }
         catch (System.Exception)
         {

         }
         return getTimestamp(sValue, format, defaultDate);
      }
      private static System.DateTime getTimestamp(string sValue, timestampFormat format, System.DateTime defaultDate)
      {
         System.DateTime result = default(System.DateTime);
         try
         {
            if (sValue.Length >= 6)
            {
               result = new System.DateTime(Convert.ToInt32(sValue.Substring(0, 4)), Convert.ToInt32(sValue.Substring(4, 2)), Convert.ToInt32(sValue.Substring(6, 2)));
               if (format != timestampFormat.DateOnly)
               {
                  int timePosStart = 8;
                  if (format == timestampFormat.DateTimeDash)
                     timePosStart = 9;
                  result = new System.DateTime(result.Year, result.Month, result.Day, Convert.ToInt32(sValue.Substring(timePosStart, 2)), Convert.ToInt32(sValue.Substring(timePosStart + 3, 2)), Convert.ToInt32(sValue.Substring(timePosStart + 6, 2)));
               }
            }
            else
            {
               result = defaultDate;
            }
         }
         catch (System.Exception)
         {

            result = defaultDate;
         }
         return result;
      }



      public void PrintStatus(qfCXOrderStatus OrderStatus)
      {
         string s = null;
         s = "Status: ";

         s += " ,ClientOrderId: " + OrderStatus.ClientOrderId;
         s += " ,ExecType: " + OrderStatus.ExecType.ToString();
         s += " ,Symbol: " + OrderStatus.Symbol;
         if (OrderStatus.legs != null && OrderStatus.legs.Count > 0)
            s += " ,side: " + OrderStatus.legs[0].LegSide.ToString();
         s += " ,Quantity: " + OrderStatus.legs[0].Quantity.ToString();
         s += " ,Fill: " + OrderStatus.Fill.ToString();

         logger.Info(s);
      }

      public BaseFIX(bool IsInitiator = true, bool ResetOnLogon = true)
      {
         try
         {
            logger = LogManager.GetLogger(GetType().FullName);
            bIsInitiator = IsInitiator;
            bResetOnLogon = ResetOnLogon;



            HashSubscriptionData = new Hashtable();
         }
         catch (System.Exception ex)
         {
            logger.Error("Error creating new BaseFIX: " + ex.Message);

         }

      }

      public virtual void dispose()
      {
         try
         {
            logger.Info("Disposing FIX Application " + this.GetType().Name);

            Disconnect();


         }
         catch (System.Exception ex)
         {
            logger.Error("Error disposing " + this.GetType().Name + ": " + ex.Message);
         }
      }
      void System.IDisposable.Dispose()
      {
         dispose();
      }

      protected QuickFix.SessionID getSessionID()
      {
         return this.getSessionID("");
      }
      protected QuickFix.SessionID getSessionID(string sessionQualifier)
      {
         SessionID sessionID = null;
         try
         {
            if (bIsInitiator)
            {

               foreach (SessionID sessQualifier in objInitiator.GetSessionIDs())
               {
                  if (sessionQualifier.Equals("") || sessionQualifier.Equals(sessQualifier))
                  {
                     sessionID = sessQualifier;
                     break; // TODO: might not be correct. Was : Exit For
                  }
               }
            }
            else
            {

               foreach (SessionID sessQualifier in objAcceptor.GetSessionIDs())
               {
                  if (sessionQualifier.Equals("") || sessionQualifier.Equals(sessQualifier))
                  {
                     sessionID = sessQualifier;
                     break; // TODO: might not be correct. Was : Exit For
                  }
               }

            }

         }
         catch (System.Exception ex)
         {
            logger.Error("Failed to get SessionID: " + ex.Message);
         }
         return sessionID;

      }
      public SessionSettings GetSessionSettings()
      {
         return objSettings;
      }

      private delegate void dlgConnect(string SettingsFile);
      public void ConnectOrListen(string SettingsFile)
      {
         if (bIsInitiator == true)
         {
            this.Connect(SettingsFile);
         }
         else if (bIsInitiator == false)
         {
            this.Listen(SettingsFile);
         }
      }
      ///<summary>
      /// Connects to a FIX provider, using the settings in the file passed in
      ///</summary>
      public void Connect(string SettingsFile)
      {


         try
         {
            // make sure the connection type is correct
            if (bIsInitiator == false)
            {
               logger.Info("Cannot Connect an acceptor connection, call Listen instead");
               return;
            }


            if (objInitiator == null)
            {
               FileStoreFactory objStoreFactory = default(FileStoreFactory);
               FileLogFactory objLogFactory = default(FileLogFactory);
               DefaultMessageFactory objMessageFactory = default(DefaultMessageFactory);

               logger.Info("Settings file:  '" + SettingsFile + "'");
               objSettings = new SessionSettings(SettingsFile);

               objStoreFactory = new FileStoreFactory(objSettings);
               objLogFactory = new FileLogFactory(objSettings);

               objMessageFactory = new DefaultMessageFactory();

               // Needs to be SocketInititiator rather than ThreadedSocketInitiator because Threaded results in a handle leak, 
               // never letting go of TCP connections in certain circumstances (see also bug 5378)
               // ref: http://old.nabble.com/file-descriptor-memory-leak-in-ThreadedSocketInitiator-td8219182.html#a8219182
               objInitiator = new QuickFix.Transport.SocketInitiator(this, objStoreFactory, objSettings, objLogFactory, objMessageFactory);

            }


            if (blnStarted == false)
            {
               // This spawns the initiator thread and sends a Logon to the counterparty
               objInitiator.Start();
               blnStarted = true;

               if (objSettings.Get(getSessionID()).Has("TargetSubID"))
               {
                  targetSubID = objSettings.Get(getSessionID()).GetString("TargetSubID");
               }

               if (objSettings.Get(getSessionID()).Has("PriceImprovement"))
               {
                  usingPriceImprovement = objSettings.Get(getSessionID()).GetBool("PriceImprovement");
               }
               else
               {
                  usingPriceImprovement = false;
               }

               if (objSettings.Get(getSessionID()).Has("AccountID"))
               {
                  accountID = objSettings.Get(getSessionID()).GetString("AccountID");
               }

               if (objSettings.Get(getSessionID()).Has("SenderSubID"))
               {
                  senderSubID = objSettings.Get(getSessionID()).GetString("SenderSubID");
               }

               if (objSettings.Get(getSessionID()).Has("NewOrderDelayMs"))
               {
                  try
                  {
                     NewOrderDelayMs = Convert.ToInt32(objSettings.Get(getSessionID()).GetString("NewOrderDelayMs"));
                  }
                  catch (System.Exception ex)
                  {
                     logger.Error("Error in converting NewOrderDelayMs value from String to integer : " + ex.Message);
                     NewOrderDelayMs = 0;
                  }
               }
               if (objSettings.Get(getSessionID()).Has("IncludeMidPremium"))
               {
                  string includeMidPremium = objSettings.Get(getSessionID()).GetString("IncludeMidPremium");
                  if (includeMidPremium.Equals("Y") | includeMidPremium.Equals("y"))
                  {
                     this.includeMidPremium = true;
                  }
               }
            }
         }
         catch (System.Exception ex)
         {
            logger.Error("Failed to create SocketInitiator: " + ex.Message);
         }

      }

      public string getAccountID(string accountId)
      {
         // accountID is passed from the ini file. If it is not an empty string and also not nothing, then return accountID else 
         // return the accountID that Has been passed in after performing similar checks
         string returnAccountID = "";
         if ((accountID != null) && accountID.Length > 1)
         {
            returnAccountID = accountID;
         }
         else if ((accountId != null) && accountId.Length > 1)
         {
            returnAccountID = accountId;
         }
         return returnAccountID;

      }

      ///<summary>
      /// Listens for incoming FIX sessions, using the settings in the file passed in
      ///</summary>
      public void Listen(string SettingsFile)
      {

         try
         {
            // make sure the connection type is correct
            if (bIsInitiator == true)
            {
               logger.Info("Cannot Listen on an acceptor connection, call Connect instead");
               return;
            }


            if (objAcceptor == null)
            {
               FileStoreFactory objStoreFactory = default(FileStoreFactory);
               FileLogFactory objLogFactory = default(FileLogFactory);
               DefaultMessageFactory objMessageFactory = default(DefaultMessageFactory);

               logger.Info("Settings file:  '" + SettingsFile + "'");
               objSettings = new SessionSettings(SettingsFile);

               objStoreFactory = new FileStoreFactory(objSettings);
               objLogFactory = new FileLogFactory(objSettings);

               objMessageFactory = new DefaultMessageFactory();

               // While the initiator needs to be SocketInitiator instead of ThreadedSocketInitiator because of leaked socket handles (see comment
               // above the New SocketInitiator for more info), the Listener will not be prone to the same problem because it does not get called
               // over and over again with the .start() and .stop(), instead just listening for inbound connections.
               objAcceptor = new ThreadedSocketAcceptor(this, objStoreFactory, objSettings, objLogFactory, objMessageFactory);

            }


            if (blnStarted == false)
            {
               // This spawns the initiator thread and sends a Logon to the counterparty
               objAcceptor.Start();
               blnStarted = true;

               if (objSettings.Get(getSessionID()).Has("TargetSubID"))
               {
                  targetSubID = objSettings.Get(getSessionID()).GetString("TargetSubID");
               }

               if (objSettings.Get(getSessionID()).Has("PriceImprovement"))
               {
                  usingPriceImprovement = objSettings.Get(getSessionID()).GetBool("PriceImprovement");
               }
               else
               {
                  usingPriceImprovement = false;
               }

               if (objSettings.Get(getSessionID()).Has("AccountID"))
               {
                  accountID = objSettings.Get(getSessionID()).GetString("AccountID");
               }

            }
         }
         catch (System.Exception ex)
         {
            logger.Error("Failed to create ThreadedSocketAcceptor: " + ex.Message);
         }

      }




      private delegate void dlgDisconnect();

      public void Disconnect()
      {

         try
         {
            if (this.Connected)
            {
               if (bIsInitiator == true)
               {
                  objInitiator.Stop();
               }
               else
               {
                  objAcceptor.Stop();
               }

               blnStarted = false;
            }
         }
         catch (System.Exception ex)
         {
            logger.Error("Exception trying to disconnect " + this.GetType().Name + ": " + ex.Message);
         }

      }

      public bool Connected
      {
         get
         {
            bool bReturn = false;

            QuickFix.Session tmpSession = default(QuickFix.Session);
            int iCount = 0;

            try
            {
               if (bIsInitiator)
               {
                  iCount = objInitiator.GetSessionIDs().Count;
               }
               else
               {
                  iCount = objAcceptor.GetSessionIDs().Count;
               }

               if (iCount > 0)
               {
                  foreach (SessionID localSessID in objInitiator.GetSessionIDs())
                  {

                     tmpSession = QuickFix.Session.LookupSession(localSessID);

                     if (tmpSession.IsLoggedOn)
                     {
                        return true;
                     }
                  }

                  foreach (SessionID localSessID in objAcceptor.GetSessionIDs())
                  {

                     tmpSession = QuickFix.Session.LookupSession(localSessID);

                     if (tmpSession.IsLoggedOn)
                     {
                        return true;
                     }
                  }
               }
               else
               {
                  bReturn = false;
               }
            }
            catch (System.Exception)
            {
               bReturn = false;
            }

            return bReturn;
         }
      }

      public virtual bool IsSubscribed(string symbol)
      {

         bool blnReturn = false;
         subscriptionStruct subData = default(subscriptionStruct);

         if (HashSubscriptionData.Count > 0)
         {
            lock (HashSubscriptionData.SyncRoot)
            {
               if (HashSubscriptionData.ContainsKey(symbol))
               {
                  subData = (subscriptionStruct)HashSubscriptionData[symbol];
                  blnReturn = subData.Subscribed;
               }
            }
         }
         return blnReturn;


      }

      public virtual void SetSubscribed(string symbol, bool value)
      {
         subscriptionStruct subData = default(subscriptionStruct);
         subData.Symbol = symbol;
         subData.Subscribed = value;
         lock (HashSubscriptionData.SyncRoot)
         {
            HashSubscriptionData[symbol] = subData;
         }
      }



      ///<summary>
      /// Subscribes to given symbol
      ///</summary>


      protected delegate void dlgSubscribeSymbol_shared(string Symbol, bool blnSubscribe);

      public enum quoteCancelResponseType
      {
         Unknown,
         NoResponse,
         Yes
      }


      /// <summary>
      /// Responds to Client Quote Cancel
      /// </summary>
      /// <value></value>
      /// <returns>If True, this implementation will send a response to sending a quote cancel</returns>
      /// <remarks></remarks>
      public abstract quoteCancelResponseType RespondsToClientQuoteCancel { get; }

      ///<summary>
      /// Called when an administrative message is received from a counterparty
      ///</summary>
      ///<remarks>
      /// Throwing a RejectLogon will disconnect the counterparty
      ///</remarks>
      void IApplication.FromAdmin(QuickFix.Message Message, QuickFix.SessionID sessionID)
      {
         try
         {
            Type messageType = Message.GetType();
            // this triggers the onMessage callbacks for the Admin messages
            Crack(Message, sessionID);

            onMessageFromCounterParty(Message, sessionID, null);
         }
         catch (System.Exception ex)
         {
            logger.Error(ex.Message);
            onMessageFromCounterParty(Message, sessionID, ex);
         }
      }

      ///<summary>
      /// Called when an application message is received from a counterparty. 
      /// Crack distinguishes what type of message it is and calls the appropiate OnMessage that is overridden from MessageCracker
      /// If the onMessage for the specific message type is NOT implemented, than an UnsupportedMessageType  exception is thrown.
      ///</summary>
      ///<remarks>
      /// Several exceptions can be thrown here to reject the message from the counterparty, see the quickfix documentation 
      /// (http://www.quickfixengine.org/quickfix/doc/html/application.html)
      ///</remarks>
      void IApplication.FromApp(QuickFix.Message Message, QuickFix.SessionID sessionID)
      {
         try
         {
            Type messageType = Message.GetType();
            // this triggers the onMessage callbacks for the Application messages
            Crack(Message, sessionID);

            onMessageFromCounterParty(Message, sessionID, null);
         }
         catch (Exception ex)
         {

            logger.Error("fromApp: Exception: " + ex.ToString());
            onMessageFromCounterParty(Message, sessionID, ex);
         }
      }

      ///<summary>
      /// Gets called when quickfix creates a new session. A session comes into and remains in existence for the life of the application. 
      /// Sessions exist whether or not a counter party is connected to it. As soon as a session is created, you can begin sending messages to it. 
      /// If no one is logged on, the messages will be sent at the time a connection is established with the counterparty. 
      ///</summary>
      void IApplication.OnCreate(QuickFix.SessionID sessionID)
      {
      }

      ///<summary>
      /// Notifies you when a valid logon Has been established with a counter party. 
      /// This is called when a connection Has been established and the FIX logon process Has completed with both parties exchanging valid logon messages. 
      ///</summary>
      void IApplication.OnLogon(QuickFix.SessionID sessionID)
      {
         try
         {
            logger.Info("++++++++++++++> onLogon sessionID: ");
            onMessageLogon(sessionID);
            logger.Info("++++++++++++++> After sending onLogon sessionID: ");
         }
         catch (System.Exception ex)
         {
            logger.Error("Failed raising Logon event: " + ex.Message);
         }
      }

      protected virtual void onMessageLogon(QuickFix.SessionID sessionID)
      {
         try
         {
            logger.Info("-------------> onMessageLogon sessionID: ");
            if (StatusChange != null)
            {
               StatusChange(BaseFIX.qfStatus.qfLogon);
            }
            logger.Info("****> onMessageLogon after Status changed: ");
            if (MessageFromCounterParty != null)
            {
               MessageFromCounterParty("onLogon", "Logged on " + this.GetType().Name, null);
            }
            logger.Info("******> onMessageLogon after MessageFromCounterParty: ");
         }
         catch (System.Exception ex)
         {
            logger.Error("Failure in onMessageLogon : " + ex.Message);
         }
      }

      ///<summary>
      /// Called when a session is no longer online (due to logout, forced termination, network connectivity, etc)
      ///</summary>
      void IApplication.OnLogout(QuickFix.SessionID sessionID)
      {
         try
         {
            onMessageLogout(sessionID);
         }
         catch (System.Exception ex)
         {
            logger.Error("Failure raising Logout event : " + ex.Message);
         }
      }

      protected virtual void onMessageLogout(QuickFix.SessionID sessionID)
      {
         try
         {
            // Clear all existing subscriptions
            HashSubscriptionData.Clear();

            if (StatusChange != null)
            {
               StatusChange(qfStatus.qfLogout);
            }
            if (MessageFromCounterParty != null)
            {
               MessageFromCounterParty("onLogout", "Logged off " + this.GetType().Name, null);
            }
         }
         catch (System.Exception ex)
         {
            logger.Error("Failure in onMessageLogout : " + ex.Message);
         }
      }



      ///<summary>
      /// Called as an administrative message is about to be sent to the counterparty
      /// If the message being sent is a Logon message then we need to raise an event for the Consumer to modify the message before sending
      ///</summary>
      void IApplication.ToAdmin(QuickFix.Message Message, QuickFix.SessionID sessionID)
      {
         try
         {
            if (Message is QuickFix.FIX40.Logon | Message is QuickFix.FIX41.Logon | Message is QuickFix.FIX42.Logon | Message is QuickFix.FIX43.Logon | Message is QuickFix.FIX44.Logon)
            {
               // Have each firm modify Logon message according to their specs before sending
               onMessageToAdminLogon(ref Message, sessionID);

               // We currently don't have a remote way to reset sequence numbers, so set this to True if the bool is set
               if (bResetOnLogon == true)
               {
                  Message.SetField(new QuickFix.Fields.ResetSeqNumFlag(bResetOnLogon));
               }
            }
            else if (Message is QuickFix.FIX40.Heartbeat | Message is QuickFix.FIX41.Heartbeat | Message is QuickFix.FIX42.Heartbeat | Message is QuickFix.FIX43.Heartbeat | Message is QuickFix.FIX44.Heartbeat)
            {
               // Have each firm modify Heartbeat message according to their specs before sending
               onMessageToAdminHeartbeat(ref Message, sessionID);

            }
            else if (Message is QuickFix.FIX40.Logout | Message is QuickFix.FIX41.Logout | Message is QuickFix.FIX42.Logout | Message is QuickFix.FIX43.Logout | Message is QuickFix.FIX44.Logout)
            {
               // Have each firm modify Logout message according to their specs before sending
               onMessageToAdminLogout(ref Message, sessionID);

            }
            else if (Message is QuickFix.FIX40.Reject | Message is QuickFix.FIX41.Reject | Message is QuickFix.FIX42.Reject | Message is QuickFix.FIX43.Reject | Message is QuickFix.FIX44.Reject)
            {
               // 
               if (Message is QuickFix.FIX44.Reject)
               {
                  QuickFix.FIX44.Reject rejMsg = (QuickFix.FIX44.Reject)Message;
                  string refMsgType = rejMsg.GetField(372);
                  GeneralFailureEventArgs e = new GeneralFailureEventArgs();
                 
                  e.errorMessage = rejMsg.GetField(58);
                  e.SetMsgType(refMsgType);
                  if (e.messageType == qfMsgType.qfExecutionReport)
                  {
                     e.quoteRequestId = lastQuoteRequestID;
                  }

                  //RaiseEvent GeneralFailure(Me, e)
               }

            }

            onMessageToCounterParty(Message, sessionID, null);
         }
         catch (System.Exception ex)
         {
            logger.Error(ex.Message);
            onMessageToCounterParty(Message, sessionID, ex);
         }
      }

      // This happens after a toAdmin message finds an outgoing Logon message and raises and event.
      // Message is passed by reference because we add fields to the Logon message here and the changed message is then sent after 
      // the BaseFIX's toAdmin is done. 
      // Firms that need to send more than just the password override this.
      protected virtual void onMessageToAdminLogon(ref QuickFix.Message message, QuickFix.SessionID sessionID)
      {
      }

      // This happens after a toAdmin message finds an outgoing Heartbeat message and raises and event.
      // Message is passed by reference because we add fields to the Heartbeat message here and the changed message is then sent after 
      // the BaseFIX's toAdmin is done. 
      // Firms that need to send more than just the password override this.
      protected virtual void onMessageToAdminHeartbeat(ref QuickFix.Message message, QuickFix.SessionID sessionID)
      {
      }



      // This happens after a toAdmin message finds an outgoing Logout message and raises and event.
      // Message is passed by reference because we add fields to the Logout message here and the changed message is then sent after 
      // the BaseFIX's toAdmin is done. 
      // Firms that need to send more than just the password override this.
      protected virtual void onMessageToAdminLogout(ref QuickFix.Message message, QuickFix.SessionID sessionID)
      {
      }

      ///<summary>
      /// Called as an application message is about to be sent to the counterparty.
      ///</summary>
      ///<remarks>
      /// If you throw a DoNotSend exception in this function, the application will not send the message.
      ///</remarks>
      void IApplication.ToApp(QuickFix.Message Message, QuickFix.SessionID sessionID)
      {
         try
         {
            // let Consumer have a chance to modify message before sending
            onMessageToApp(ref Message, sessionID);

            onMessageToCounterParty(Message, sessionID, null);
         }
         catch (System.Exception ex)
         {
            logger.Error(ex.Message);
            onMessageToCounterParty(Message, sessionID, ex);
         }
      }

      protected virtual void onMessageToApp(ref QuickFix.Message message, QuickFix.SessionID sessionID)
      {
      }


      protected virtual void onMessageFromCounterParty(object Message, QuickFix.SessionID sessionID, System.Exception ex)
      {
         string strMessage = null;
         string messageType = null;

         strMessage = Message.ToString();
         messageType = Message.GetType().ToString();

         if (MessageFromCounterParty != null)
         {
            MessageFromCounterParty(messageType, strMessage, ex);
         }
      }

      protected virtual void onMessageToCounterParty(object Message, QuickFix.SessionID sessionID, System.Exception ex)
      {
         string strMessage = null;
         string messageType = null;

         strMessage = Message.ToString();
         messageType = Message.GetType().ToString();

         if (MessageToCounterParty != null)
         {
            MessageToCounterParty(messageType, strMessage, ex);
         }
      }

      ///<summary>
      /// Procedure to process a BusinessMessageReject
      ///</summary>
      ///<remarks>
      /// The Business Message Reject message can reject an application-level message which fulfills session-level rules and 
      /// cannot be rejected via any other means. 
      ///</remarks>
      protected virtual void onMessageBusinessMessageReject(object Message, QuickFix.SessionID sessionID)
      {
         try
         {
            onStatusChange(qfStatus.qfReject);
            onMessageFromCounterParty(Message, sessionID, null);
         }
         catch (System.Exception ex)
         {
            logger.Error(ex.Message);
            onStatusChange(qfStatus.qfReject);
            onMessageFromCounterParty(Message, sessionID, ex);
         }
      }
      protected void RaiseMessageFromCounterPartyEvent(string MessageType, string Message, System.Exception ex)
      {
         if (MessageFromCounterParty != null)
         {
            MessageFromCounterParty(MessageType, Message, ex);
         }
      }
      protected void onStatusChange(BaseFIX.qfStatus Status)
      {
         if (StatusChange != null)
         {
            StatusChange(Status);
         }
      }

      // This is for outgoing market data requests
      protected void RaiseMarketData(string sym, double Bid, double Ask, string Originator, System.DateTime TradeDate)
      {
         RaiseMarketData(sym, Bid, Ask, Originator, TradeDate, "");
      }

      protected void RaiseMarketData(string sym, double Bid, double Ask, string Originator, System.DateTime TradeDate, string QuoteID)
      {
         if (MarketData != null)
         {
            MarketData(sym.ToString(), Bid, Ask, Originator, TradeDate, QuoteID);
         }
      }

      // This is for incoming quotes for RFQ
      protected void RaiseRFQQuote(RFQQuoteEventArgs e)
      {
         if (RFQQuote != null)
         {
            RFQQuote(this, e);
         }
      }

      // This is for incoming indicative quotes for RFQ
      protected void RaiseVolQuote(string sym, double BidPremium, double AskPremium, string quoteID, string quoteReqID, OptionType putOrCall, double AskStrike, double BidStrike, string tenor, double bidVol,

      double askVol, string delta, int dealMode, DateTime expDateFromLP)
      {
         if (VolQuote != null)
         {
            VolQuote(sym, BidPremium, AskPremium, quoteID, quoteReqID, putOrCall, AskStrike, BidStrike, tenor, bidVol,
            askVol, delta, dealMode, expDateFromLP);
         }
      }

      // This is for incoming indicative quotes for RFQ
      protected void RaiseIntQuote(string sym, string quoteID, string quoteReqID, string tenor, double bidIR, double askIR, string currency, DateTime providerExpDate)
      {
         if (IntQuote != null)
         {
            IntQuote(sym.ToString(), quoteID, quoteReqID, tenor, bidIR, askIR, currency, providerExpDate);
         }
      }

      // This is for incoming spot quote cancels
      protected void RaiseQuoteCancel(string sym, string quoteID, string quoteReqID, int quoteCancelType)
      {
         if (QuoteCancel != null)
         {
            QuoteCancel(sym.ToString(), quoteID, quoteReqID, quoteCancelType);
         }
      }

      // This is for incoming option quote cancels
      protected void RaiseOptionsQuoteCancel(string sym, string quoteID, string quoteReqID, int quoteCancelType, string tenor)
      {
         if (OptionsQuoteCancel != null)
         {
            OptionsQuoteCancel(sym.ToString(), quoteID, quoteReqID, quoteCancelType, tenor);
         }
      }

      // This is for incoming option quote rejects
      protected void RaiseOptionsQuoteReject(string sym, string quoteReqID, int quoteRejectReason, string quoteRejectText)
      {
         if (OptionsQuoteReject != null)
         {
            OptionsQuoteReject(sym.ToString(), quoteReqID, quoteRejectReason, quoteRejectText);
         }
      }

      protected void RaiseMassQuote(string Symbol, Hashtable massQuoteDataHashTable)
      {
         if (MassQuote != null)
         {
            MassQuote(Symbol, massQuoteDataHashTable);
         }
      }

      protected void RaiseRFQDealerIntervention(string quoteRequestID, string DIReason)
      {
         if (RFQDealerIntervention != null)
         {
            RFQDealerIntervention(quoteRequestID, DIReason);
         }
      }

      protected virtual void onMessageMarketData(object mdMessage, QuickFix.SessionID sessionID)
      {
      }

      protected virtual void onMessageExecutionReport(object message, QuickFix.SessionID sessionID)
      {
      }

      protected virtual void onMessageQuoteCancel(object message, QuickFix.SessionID sessionID)
      {
      }

      protected virtual void onMessageMarketDataRequestReject(object message, QuickFix.SessionID sessionID)
      {
      }

      protected virtual void onMessageMarketDataLogout(object message, QuickFix.SessionID sessionID)
      {
      }

      public virtual void OnMessage(QuickFix.FIX44.MarketDataIncrementalRefresh message, QuickFix.SessionID sessionID)
      {

         onMessageMarketData(message, sessionID);
      }

      public virtual void OnMessage(QuickFix.FIX43.MarketDataIncrementalRefresh message, QuickFix.SessionID session)
      {
      }


      public virtual void OnMessage(QuickFix.FIX43.BusinessMessageReject message, QuickFix.SessionID session)
      {
      }

      public virtual void OnMessage(QuickFix.FIX44.BusinessMessageReject message, QuickFix.SessionID session)
      {
      }

      public virtual void OnMessage(QuickFix.FIX50.BusinessMessageReject message, QuickFix.SessionID session)
      {
      }

      public virtual void OnMessage(QuickFix.FIX42.MarketDataIncrementalRefresh message, QuickFix.SessionID sessionID)
      {
         onMessageMarketData(message, sessionID);
      }

      public virtual void OnMessage(QuickFix.FIX44.MarketDataSnapshotFullRefresh message, QuickFix.SessionID sessionID)
      {
         onMessageMarketData(message, sessionID);
      }

      public virtual void OnMessage(QuickFix.FIX43.MarketDataSnapshotFullRefresh message, QuickFix.SessionID sessionID)
      {
         onMessageMarketData(message, sessionID);
      }

      public virtual void OnMessage(QuickFix.FIX42.MarketDataSnapshotFullRefresh message, QuickFix.SessionID sessionID)
      {
         onMessageMarketData(message, sessionID);
      }

      public virtual void OnMessage(QuickFix.FIX44.MarketDataRequest message, QuickFix.SessionID session)
      {
      }

      public virtual void OnMessage(QuickFix.FIX43.MarketDataRequest message, QuickFix.SessionID session)
      {
      }

      public virtual void OnMessage(QuickFix.FIX42.MarketDataRequest message, QuickFix.SessionID session)
      {
      }

      public virtual void OnMessage(QuickFix.FIX43.MarketDataRequestReject message, QuickFix.SessionID sessionID)
      {
         onMessageMarketDataRequestReject(message, sessionID);
      }

      public virtual void OnMessage(QuickFix.FIX44.MarketDataRequestReject message, QuickFix.SessionID sessionID)
      {
         onMessageMarketDataRequestReject(message, sessionID);
      }

      public virtual void OnMessage(QuickFix.FIX42.MarketDataRequestReject message, QuickFix.SessionID session)
      {
      }

      public virtual void OnMessage(QuickFix.FIX44.QuoteStatusReport message, QuickFix.SessionID sessionID)
      {

      }

      public virtual void OnMessage(QuickFix.FIX44.TestRequest message, SessionID sessionID)
      {

      }

      public virtual void OnMessage(QuickFix.FIX43.TestRequest message, SessionID sessionID)
      {

      }



      public virtual void OnMessage(QuickFix.FIX44.Quote message, SessionID sessionID)
      {
         onMessageMarketData(message, sessionID);
      }

      public virtual void OnMessage(QuickFix.FIX43.Quote message, QuickFix.SessionID sessionID)
      {
         onMessageMarketData(message, sessionID);
      }


      public virtual void OnMessage(QuickFix.FIX42.Quote message, QuickFix.SessionID sessionID)
      {
      }

      public virtual void OnMessage(QuickFix.FIX44.QuoteCancel message, QuickFix.SessionID sessionID)
      {
         onMessageQuoteCancel(message, sessionID);
      }
      public virtual void OnMessage(QuickFix.FIX50.QuoteCancel message, QuickFix.SessionID sessionID)
      {
         onMessageQuoteCancel(message, sessionID);
      }

      public virtual void OnMessage(QuickFix.FIX43.QuoteCancel message, QuickFix.SessionID sessionID)
      {
      }

      public virtual void OnMessage(QuickFix.FIX42.QuoteCancel message, QuickFix.SessionID sessionID)
      {
      }

      public virtual void OnMessage(QuickFix.FIX44.QuoteRequestReject message, QuickFix.SessionID sessionID)
      {
         onMessageMarketDataRequestReject(message, sessionID);
      }

      public virtual void OnMessage(QuickFix.FIX43.QuoteRequestReject message, QuickFix.SessionID sessionID)
      {
      }

      // Fix 4.2 does not have a QuoteRequestReject message

      public virtual void OnMessage(QuickFix.FIX42.Heartbeat message, QuickFix.SessionID sessionID)
      {
         if (Heartbeat != null)
         {
            Heartbeat(DateTime.UtcNow);
         }
      }

      public virtual void OnMessage(QuickFix.FIX43.Heartbeat message, QuickFix.SessionID sessionID)
      {
         if (Heartbeat != null)
         {
            Heartbeat(DateTime.UtcNow);
         }
      }

      public virtual void OnMessage(QuickFix.FIX44.Heartbeat message, QuickFix.SessionID sessionID)
      {
         if (Heartbeat != null)
         {
            Heartbeat(DateTime.UtcNow);
         }
      }

      public virtual void OnMessage(QuickFix.FIX44.Logon message, QuickFix.SessionID sessionID)
      {
      }

      public virtual void OnMessage(QuickFix.FIX43.Logon message, QuickFix.SessionID sessionID)
      {
      }

      public virtual void OnMessage(QuickFix.FIX42.Logon message, QuickFix.SessionID session)
      {
      }

      public virtual void OnMessage(QuickFix.FIX44.Logout message, QuickFix.SessionID sessionID)
      {
         onMessageMarketDataLogout(message, sessionID);
      }

      public virtual void OnMessage(QuickFix.FIX43.Logout message, QuickFix.SessionID sessionID)
      {
         onMessageMarketDataLogout(message, sessionID);
      }

      public virtual void OnMessage(QuickFix.FIX42.Logout message, QuickFix.SessionID sessionID)
      {
         onMessageMarketDataLogout(message, sessionID);
      }

      public virtual void OnMessage(QuickFix.FIX50.ExecutionReport message, QuickFix.SessionID sessionID)
      {
         onMessageExecutionReport(message, sessionID);
      }
      public virtual void OnMessage(QuickFix.FIX44.ExecutionReport message, QuickFix.SessionID sessionID)
      {
         onMessageExecutionReport(message, sessionID);
      }

      public virtual void OnMessage(QuickFix.FIX43.ExecutionReport message, QuickFix.SessionID sessionID)
      {
         onMessageExecutionReport(message, sessionID);
      }

      public virtual void OnMessage(QuickFix.FIX42.ExecutionReport message, QuickFix.SessionID sessionID)
      {
         onMessageExecutionReport(message, sessionID);
      }



      protected void RaiseOrderStatusUpdate(qfCXOrderStatus OrderStatus)
      {
         if (OrderStatusUpdate != null)
         {
            OrderStatusUpdate(OrderStatus);
         }
      }


      public void SendQuoteRequest(IStrategy orders, qfQuoteType quoteType, qfQuoteDataType quoteDataType)
      {

         lastQuoteRequestID = orders.QuoteRequestId;
         QuoteRequest(orders, quoteType, quoteDataType);
      }

      protected virtual void QuoteRequest(IStrategy orders, qfQuoteType quoteType, qfQuoteDataType quoteDataType)
      {
         throw new System.Exception("Function QuoteRequest Has not been over-riden in the base class");
      }
      public virtual void QuoteRequest(string quoteReqID, SideEnum side, OptionType optionType, string Symbol, int Quantity, string settlDate, qfQuoteType quoteType, qfQuoteDataType quoteDataType, string currency, string delta)
      {
         throw new System.Exception("Function QuoteRequest Has not been over-riden in the base class");
      }
      public virtual void QuoteRequest(string Symbol, OptionType optionType, string[] delta, string[] tenor, string premiumCurrency)
      {
         throw new System.Exception("Function QuoteRequest Has not been over-riden in the base class");
      }
      public virtual void QuoteRequest(string Symbol, string MDRequestID)
      {
         throw new System.Exception("Function QuoteRequest Has not been over-riden in the base class");
      }
      public virtual void SendQuoteCancel(string quoteReqID, string quoteID, string symbol, string accountID, string tradingSessionID, IStrategy orders = null)
      {
      }
      public virtual void QuoteResponse(string Symbol, string quoteReqID, string quoteID, int response, string maturity, qfQuoteDataType quoteDataType, string accountID)
      {
      }


      public virtual void MassQuoteRequest(string Symbol, string quoteReqID)
      {
      }


      public virtual void sendOrderStatusRequest(string clientOrderID)
      {
      }


      public virtual void sendTradeCaptureReportRequest(string tradeRequestID, DateTime startTime, DateTime endTime)
      {
      }
      

      public virtual void RequestOrderList()
      {
      }




      // Retrieve the sessionID based on the senderCompID. If only 1 session exists, then return that session.
      // If more than 1 sessions exists, then we compare the configuredSenderCompID to the senderCompID and if they match, return that
      // sessionID
      public QuickFix.SessionID getSessionIDfromSenderCompID(string configuredSenderCompID)
      {
         if (configuredSenderCompID == null)
         {
            configuredSenderCompID = "";
         }
         SessionID sessionID = null;
         try
         {
            if (bIsInitiator)
            {
               foreach (SessionID sess in objInitiator.GetSessionIDs())
               {
                  string senderCompID = sess.SenderCompID;
                  if (configuredSenderCompID == "" || configuredSenderCompID.Equals(senderCompID))
                  {
                     sessionID = sess;
                     break; // TODO: might not be correct. Was : Exit For
                  }
               }

            }
            else
            {
               foreach (SessionID sess in objAcceptor.GetSessionIDs())
               {
                  string senderCompID = sess.SenderCompID;
                  if (configuredSenderCompID == "" || configuredSenderCompID.Equals(senderCompID))
                  {
                     sessionID = sess;
                     break; // TODO: might not be correct. Was : Exit For
                  }
               }
            }
         }
         catch (System.Exception ex)
         {
            logger.Error("Error retrieving the sessionID from senderCompID: " + ex.Message);
         }
         return sessionID;

      }
      // Returns the onBehalfOfCompID for the session.
      public string onBehalfOfCompID(SessionID sessionID, string compId)
      {
         string retOnBehalfOfCompID = "";
         if (!string.IsNullOrWhiteSpace(compId))
            return compId;
         if (objSettings.Get(sessionID).Has("OnBehalfOfCompID"))
         {
            retOnBehalfOfCompID = objSettings.Get(sessionID).GetString("OnBehalfOfCompID");
         }
         return retOnBehalfOfCompID;

      }

      public string onBehalfOfSubID(SessionID sessionID, string subId)
      {
         string retOnBehalfOfCompID = "";
         if (!string.IsNullOrWhiteSpace(subId))
            return subId;
         if (objSettings.Get(sessionID).Has("OnBehalfOfSubID"))
         {
            retOnBehalfOfCompID = objSettings.Get(sessionID).GetString("OnBehalfOfSubID");
         }
         return retOnBehalfOfCompID;
      }

      // Returns the getSenderSubID for the session.
      public string getSenderSubID(QuickFix.SessionID sessionID)
      {
         string senderSubID = "";
         if (objSettings.Get(sessionID).Has("SenderSubID"))
         {
            senderSubID = objSettings.Get(sessionID).GetString("SenderSubID");
         }
         return senderSubID;
      }

      public string getSenderSubID2(QuickFix.SessionID sessionID)
      {
         string senderSubID = "";
         if (objSettings.Get(sessionID).Has("SenderSubID2"))
         {
            senderSubID = objSettings.Get(sessionID).GetString("SenderSubID2");
         }
         return senderSubID;
      }

      public string getSenderLocationID(QuickFix.SessionID sessionID)
      {
         string SenderLocationID = "";
         if (objSettings.Get(sessionID).Has("SenderLocationID"))
         {
            SenderLocationID = objSettings.Get(sessionID).GetString("SenderLocationID");
         }
         return SenderLocationID;

      }

      public string getSenderLocationID2(QuickFix.SessionID sessionID)
      {
         string SenderLocationID = "";
         if (objSettings.Get(sessionID).Has("SenderLocationID2"))
         {
            SenderLocationID = objSettings.Get(sessionID).GetString("SenderLocationID2");
         }
         return SenderLocationID;

      }

      public string PartyID(QuickFix.SessionID sessionID)
      {
         string result = "";
         if (objSettings.Get(sessionID).Has("PartyID"))
         {
            result = objSettings.Get(sessionID).GetString("PartyID");
         }
         return result;
      }









   }
}