import sys
import time
import _thread
import argparse
import quickfix as fix
import urllib3
import simplejson as json
from api.restapi import createOrder, authenticateMe

ECHO_DEBUG=False
SKIP_AUTHENTICATION=False
if ECHO_DEBUG:
    from tools.echo import echo
else:
    def echo(f):
        return f

class Application(fix.Application):
    orderID = 0
    execID = 0
    accessKey = ""
    secretKey = ""

    @echo
    def onCreate(self, sessionID): return

    @echo
    def onLogon(self, sessionID): 
        print("logged on")
        return

    @echo
    def onLogout(self, sessionID): return

    @echo
    def toAdmin(self, message, sessionID): 
        print("Sending the following admin message: %s" % message.toString())
        return

    @echo
    def fromAdmin(self, message, sessionID):   
        if SKIP_AUTHENTICATION:
            print("Received the following admin message: %s" % message.toString())
            return

        beginString = fix.BeginString()
        msgType = fix.MsgType()
        msgSeqNum = fix.MsgSeqNum()
         
        message.getHeader().getField(beginString)
        message.getHeader().getField(msgType)
        message.getHeader().getField(msgSeqNum)

        if msgType.getString() == fix.MsgType_Logon:
            try:
                response = authenticateMe('Dsfzxj7juZTogLSvCERSKVP574Zw762nHJyquLxg', 'kLgHtx0jz7sdGtUPxnIQygqZBZM4zABTxpq8VDa7')
                jResponse = json.loads(response)
                if 'error' in jResponse and jResponse['error']:
                    raise fix.RejectLogon("wrong signature")
            except KeyboardInterrupt:
                return
            except Exception as e:
                print(e)
                raise fix.RejectLogon("failed to authenticate on server. Please contact istox IT!")

        print("Received the following admin message: %s" % message.toString())
        return

    @echo
    def toApp(self, message, sessionID): 
        print("Sending the following message: %s" % message.toString())
        return

    @echo
    def fromApp(self, message, sessionID):
        print("Received the following message: %s" % message.toString())
        beginString = fix.BeginString()
        msgType = fix.MsgType()
        msgSeqNum = fix.MsgSeqNum()
        message.getHeader().getField(beginString)
        message.getHeader().getField(msgType)
        message.getHeader().getField(msgSeqNum)

        print("Message type = %s" % msgType.getString())

        if msgType.getString() == fix.MsgType_NewOrderSingle:
            print("New Order received")
            executionReport = self.getExecutionReportForNewOrder2(message)
            print("Execution report to send: %s" % executionReport.toString())
            self.sendReport(executionReport, sessionID)
        elif msgType.getString() == fix.MsgType_OrderCancelRequest:
            print("Cancel Order received")

            executionReport = self.getExecutionReportForCancelOrder(message)
            print("Execution report to send: %s" % executionReport.toString())
            self.sendReport(executionReport, sessionID)

            # use below 3 lines and comment out above 3 if you want to send order cancel reject message
            # orderCancelReject = self.getOrderCancelReject(message)
            # print("Order cancel reject to send: %s" % orderCancelReject.toString())
            # self.sendReport(orderCancelReject, sessionID)
        elif msgType.getString() == fix.MsgType_OrderStatusRequest:
            print("Order status request received")
            executionReport = self.getExecutionReportForStatusRequest(message)
            print("Execution report to send: %s" % executionReport.toString())
            self.sendReport(executionReport, sessionID)
        else:
            print("Unsupported MsgType")
            reject = fix.Message()
            reject.getHeader().setField(beginString)
            reject.getHeader().setField(fix.MsgType(fix.MsgType_Reject))
            reject.setField(fix.RefMsgType(msgType.getString()))
            reject.setField(fix.RefSeqNum(msgSeqNum.getValue())) #45 = RefSeqNum
            reject.setField(fix.SessionRejectReason(fix.SessionRejectReason_INVALID_MSGTYPE)) #373 = 11 INVALID_MSGTYPE
            reject.setField(fix.Text("iSTOX FIX does not support this message type")) 
            self.sendReport(reject, sessionID)

    @echo
    def genOrderID(self):
        self.orderID = self.orderID+1
        return str(self.orderID)

    @echo
    def genExecID(self):
        self.execID = self.execID+1
        return str(self.execID)

    @echo
    def getExecutionReportForNewOrder(self, message):

        beginString = fix.BeginString()
        message.getHeader().getField( beginString)

        symbol = fix.Symbol()
        side = fix.Side()
        ordType = fix.OrdType()
        orderQty = fix.OrderQty()
        price = fix.Price()
        clOrdID = fix.ClOrdID()
            
        message.getField(ordType)
        if ordType.getValue() != fix.OrdType_LIMIT:
            raise fix.IncorrectTagValue(ordType.getField())

        message.getField(symbol)
        message.getField(side)
        message.getField(orderQty)
        message.getField(price)
        message.getField(clOrdID)

        executionReport = fix.Message()
        executionReport.getHeader().setField(beginString)
        executionReport.getHeader().setField(fix.MsgType(fix.MsgType_ExecutionReport))

        executionReport.setField(fix.OrderID(self.genOrderID()))
        executionReport.setField(fix.ExecID(self.genExecID()))
        executionReport.setField(fix.OrdStatus(fix.OrdStatus_FILLED))
        executionReport.setField(symbol)
        executionReport.setField(side)
        executionReport.setField(fix.CumQty(orderQty.getValue()))
        executionReport.setField(fix.AvgPx(price.getValue()))
        executionReport.setField(fix.LastShares(orderQty.getValue()))
        executionReport.setField(fix.LastPx(price.getValue()))
        executionReport.setField(clOrdID)
        executionReport.setField(orderQty)
        executionReport.setField(fix.Text("New order accepted!"))

        # Since FIX 4.3, ExecTransType is killed and the values are moved to ExecType
        if beginString.getValue() == fix.BeginString_FIX40 or beginString.getValue() == fix.BeginString_FIX41 or beginString.getValue() == fix.BeginString_FIX42:
            executionReport.setField(fix.ExecTransType(fix.ExecTransType_NEW))

        # ExecType and LeavesQty fields only existsince FIX 4.1
        if beginString.getValue() >= fix.BeginString_FIX41:
            if beginString.getValue() <= fix.BeginString_FIX42:
                executionReport.setField(fix.ExecType(fix.ExecType_FILL)) #150=2 FILL  (or 1 PARTIAL_FILL)
            else:
                # FILL and PARTIAL_FILL are removed and replaced by TRADE (F) since FIX 4.3 as these info can be retrieved from OrdStatus field
                executionReport.setField(fix.ExecType(fix.ExecType_TRADE)) #150=F TRADE 
            executionReport.setField( fix.LeavesQty(0) )

        return executionReport

    @echo
    def getExecutionReportForNewOrder2(self, message):

        beginString = fix.BeginString()
        message.getHeader().getField(beginString)
        msgSeqNum = fix.MsgSeqNum()
        message.getHeader().getField(msgSeqNum)


        symbol = fix.Symbol()
        side = fix.Side()
        ordType = fix.OrdType()
        orderQty = fix.OrderQty()
        price = fix.Price()
        clOrdID = fix.ClOrdID()
            
        message.getField(ordType)
        # todo handle market order
        if ordType.getValue() != fix.OrdType_LIMIT:
            raise fix.IncorrectTagValue(ordType.getField())

        message.getField(symbol)
        message.getField(side)
        message.getField(orderQty)
        message.getField(price)
        message.getField(clOrdID)
        
        data = {}
        data['market'] = symbol.getValue()
        data['price'] = price.getValue()
        data['side'] = 'buy' if side.getValue() == fix.Side_BUY else 'sell'
        data['volume'] = orderQty.getValue()
        response = createOrder('Dsfzxj7juZTogLSvCERSKVP574Zw762nHJyquLxg', 'kLgHtx0jz7sdGtUPxnIQygqZBZM4zABTxpq8VDa7', data)

        jResponse = json.loads(response)

        executionReport = fix.Message()
        executionReport.getHeader().setField(beginString)
        executionReport.getHeader().setField(fix.MsgType(fix.MsgType_ExecutionReport))  
        # todo exec id?
        executionReport.setField(fix.ExecID(self.genExecID()))
        executionReport.setField(symbol)
        executionReport.setField(side)
        executionReport.setField(fix.CumQty(orderQty.getValue()))
        executionReport.setField(fix.AvgPx(price.getValue()))
        executionReport.setField(fix.LastShares(orderQty.getValue()))
        executionReport.setField(fix.LastPx(price.getValue()))
        # todo save Client order id to DB?
        executionReport.setField(clOrdID)
        executionReport.setField(orderQty)

        if 'error' in jResponse and jResponse['error']:
            executionReport.setField(fix.OrderID("nil"))
            executionReport.setField(fix.OrdStatus(fix.OrdStatus_REJECTED))
            executionReport.setField(fix.Text(jResponse['error']['message']))
            # todo Reject reason for each case
            executionReport.setField(fix.OrdRejReason(fix.OrdRejReason_UNKNOWN_SYMBOL))
        else:
            executionReport.setField(fix.OrderID(str(jResponse['id'])))
            # todo check trades_count from json response and set FILL status?
            executionReport.setField(fix.OrdStatus(fix.OrdStatus_NEW))
            executionReport.setField(fix.Text('New order accpeted!'))

         # Since FIX 4.3, ExecTransType is killed and the values are moved to ExecType
        if beginString.getValue() == fix.BeginString_FIX40 or beginString.getValue() == fix.BeginString_FIX41 or beginString.getValue() == fix.BeginString_FIX42:
            executionReport.setField(fix.ExecTransType(fix.ExecTransType_NEW))

        # todo check trades_count from json response and set FILL status?
        # ExecType and LeavesQty fields only existsince FIX 4.1
        if beginString.getValue() >= fix.BeginString_FIX41:
            if beginString.getValue() <= fix.BeginString_FIX42:
                executionReport.setField(fix.ExecType(fix.ExecType_FILL)) #150=2 FILL  (or 1 PARTIAL_FILL)
            else:
                # FILL and PARTIAL_FILL are removed and replaced by TRADE (F) since FIX 4.3 as these info can be retrieved from OrdStatus field
                executionReport.setField(fix.ExecType(fix.ExecType_TRADE)) #150=F TRADE 
            executionReport.setField( fix.LeavesQty(0) )

        """if 'error' in jResponse and jResponse['error']:
            reject = fix.Message()
            reject.getHeader().setField(beginString)
            reject.getHeader().setField(fix.MsgType(fix.MsgType_Reject))
            reject.setField(fix.RefMsgType(fix.MsgType_NewOrderSingle))
            reject.setField(fix.RefSeqNum(msgSeqNum.getValue())) #45 = RefSeqNum
            reject.setField(fix.SessionRejectReason(fix.SessionRejectReason_OTHER)) #373 = 99 OTHER
            reject.setField(fix.Text(jResponse['message'])) 
            return reject """ 

        return executionReport

    @echo
    def getExecutionReportForCancelOrder(self, message):

        beginString = fix.BeginString()
        message.getHeader().getField(beginString)

        symbol = fix.Symbol()
        side = fix.Side()
        clOrdID = fix.ClOrdID()
            
        message.getField(symbol)
        message.getField(side)
        message.getField(clOrdID)

        executionReport = fix.Message()
        executionReport.getHeader().setField(beginString)
        executionReport.getHeader().setField(fix.MsgType(fix.MsgType_ExecutionReport))

        executionReport.setField(fix.OrderID(self.genOrderID()))
        executionReport.setField(fix.ExecID(self.genExecID()))
        executionReport.setField(fix.OrdType(fix.OrdType_LIMIT)) #40=2 Limit order
        executionReport.setField(fix.OrderQty(100)) #38=100
        executionReport.setField(fix.Price(10)) #44=10
        executionReport.setField(fix.OrdStatus(fix.OrdStatus_FILLED))
        executionReport.setField(symbol)
        executionReport.setField(side)
        executionReport.setField(fix.AvgPx(10)) #6=10
        executionReport.setField(fix.CumQty(100)) #14=100
        executionReport.setField(clOrdID )
        executionReport.setField(fix.Text("Order cancelled!"))

        # Since FIX 4.3, ExecTransType values are moved to ExecType
        if beginString.getValue() == fix.BeginString_FIX40 or beginString.getValue() == fix.BeginString_FIX41 or beginString.getValue() == fix.BeginString_FIX42:
            executionReport.setField(fix.ExecTransType(fix.ExecTransType_CANCEL))

        # ExecType and LeavesQty fields only existsince FIX 4.1
        if beginString.getValue() >= fix.BeginString_FIX41:
            executionReport.setField(fix.ExecType(fix.ExecType_CANCELED)) #150=4 CANCELED
            executionReport.setField(fix.LeavesQty(0))  #151=0

        return executionReport

    @echo
    def getExecutionReportForStatusRequest(self, message):

        beginString = fix.BeginString()
        message.getHeader().getField(beginString)

        clOrdID = fix.ClOrdID()
        message.getField(clOrdID)

        executionReport = fix.Message()
        executionReport.getHeader().setField(beginString)
        executionReport.getHeader().setField(fix.MsgType(fix.MsgType_ExecutionReport))

        executionReport.setField(fix.Symbol("ABCD"))
        executionReport.setField(fix.Side(fix.Side_BUY))  #43=1 Buy
        executionReport.setField(fix.OrderID(self.genOrderID()))
        executionReport.setField(fix.ExecID(self.genExecID()))
        executionReport.setField(fix.OrdType(fix.OrdType_LIMIT)) #40=2 Limit order
        executionReport.setField(fix.OrderQty(100)) #38=100
        executionReport.setField(fix.Price(10)) #44=10
        executionReport.setField(fix.OrdStatus(fix.OrdStatus_FILLED))
        executionReport.setField(fix.AvgPx(10)) #6=10
        executionReport.setField(fix.CumQty(100)) #14=100
        executionReport.setField(clOrdID)
        executionReport.setField(fix.Text("Order status retrieved!"))

        # Since FIX 4.3, ExecTransType values are moved to ExecType
        if beginString.getValue() == fix.BeginString_FIX40 or beginString.getValue() == fix.BeginString_FIX41 or beginString.getValue() == fix.BeginString_FIX42:
            executionReport.setField(fix.ExecTransType(fix.ExecTransType_STATUS))
        
        # ExecType and LeavesQty fields only existsince FIX 4.1
        if beginString.getValue() >= fix.BeginString_FIX41:
            if beginString.getValue() <= fix.BeginString_FIX42:
                executionReport.setField(fix.ExecType(fix.ExecType_FILL)) #150=2 FILL  (or 1 PARTIAL_FILL)
            else:
                # FILL and PARTIAL_FILL are removed and replaced by TRADE (F) since FIX 4.3 as these info can be retrieved from OrdStatus field
                executionReport.setField(fix.ExecType(fix.ExecType_TRADE)) #150=F TRADE 
            executionReport.setField(fix.LeavesQty(0))

        return executionReport

    @echo
    def getOrderCancelReject(self, message):

        beginString = fix.BeginString()
        message.getHeader().getField( beginString )

        clOrdID = fix.ClOrdID()
        orderID = fix.OrderID()
        origClOrdID = fix.OrigClOrdID()

        message.getField(clOrdID)
        message.getField(orderID)
        message.getField(origClOrdID)

        orderCancelReject = fix.Message()
        orderCancelReject.getHeader().setField(beginString)
        orderCancelReject.getHeader().setField(fix.MsgType(fix.MsgType_OrderCancelReject))

        orderCancelReject.setField(clOrdID)
        orderCancelReject.setField(orderID)
        orderCancelReject.setField(origClOrdID)
        orderCancelReject.setField(fix.OrdStatus(fix.OrdStatus_FILLED))  #39 = 2 FILLED
        orderCancelReject.setField(fix.CxlRejReason(0)) #102=0 TOO_LATE_TO_CANCEL
        orderCancelReject.setField(fix.CxlRejResponseTo(1)) #434=1  ORDER_CANCEL_REQUEST

        return orderCancelReject

    @echo
    def sendReport(self, executionReport, sessionID):
        try:
            fix.Session.sendToTarget( executionReport, sessionID )
        except fix.SessionNotFound as e:
            print(e)
            return

def main(file_name):

    try:
        settings = fix.SessionSettings(file_name)
        application = Application()
        storeFactory = fix.FileStoreFactory(settings)
        logFactory = fix.FileLogFactory(settings)
        print('creating acceptor')
        acceptor = fix.SocketAcceptor(application, storeFactory, settings, logFactory)    
        print('starting acceptor')
        acceptor.start()

        while 1:
            time.sleep(1)
    except (fix.ConfigError, fix.RuntimeError) as e:
        print(e)

if __name__=='__main__':
    parser = argparse.ArgumentParser(description='FIX Server')
    parser.add_argument('file_name', type=str, help='Name of configuration file')
    args = parser.parse_args()
    main(args.file_name)