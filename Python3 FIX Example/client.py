import sys
import time
import _thread
import argparse
from datetime import datetime
import quickfix as fix

ECHO_DEBUG=False
if ECHO_DEBUG:
    from tools.echo import echo
else:
    def echo(f):
        return f

class Application(fix.Application):
    orderID = 0

    @echo
    def onCreate(self, sessionID):
            return

    @echo
    def onLogon(self, sessionID):
            self.sessionID = sessionID
            print ("Successful Logon to session '%s'." % sessionID.toString())
            return

    @echo
    def onLogout(self, sessionID): 
            self.sessionID = sessionID
            print ("Successful Logout from session '%s'." % sessionID.toString())
            return

    @echo
    def toAdmin(self, message, sessionID):
        
        msgType = fix.MsgType()
        message.getHeader().getField(msgType)

        if msgType.getString() == fix.MsgType_Logon:
            credential = '{"AccessKey": "Dsfzxj7juZTogLSvCERSKVP574Zw762nHJyquLxg", "SecretKey": "kLgHtx0jz7sdGtUPxnIQygqZBZM4zABTxpq8VDa7"}'
            message.setField(fix.RawData(credential))
            message.setField(fix.RawDataLength(len(credential)))

        

        print("Sending the following admin message: %s" % message.toString())
        return

    @echo
    def fromAdmin(self, message, sessionID):
        print("Recieved the following admin message: %s" % message.toString())
        return

    @echo
    def toApp(self, message, sessionID):
        print("Sending the following message: %s" % message.toString())
        return

    @echo
    def fromApp(self, message, sessionID):
        print("Recieved the following message: %s" % message.toString())
        return

    @echo
    def genOrderID(self):
        self.orderID = self.orderID+1
        return str(self.orderID)

    @echo
    def new_order(self):
        print("Creating the following order: ")
        trade = fix.Message()
        trade.getHeader().setField(fix.BeginString(fix.BeginString_FIX42)) #
        trade.getHeader().setField(fix.MsgType(fix.MsgType_NewOrderSingle)) #35=D
        trade.setField(fix.ClOrdID(self.genOrderID())) #11=Unique order id

        trade.setField(fix.HandlInst(fix.HandlInst_MANUAL_ORDER_BEST_EXECUTION)) #21=3 (Manual order, best executiona)
        trade.setField(fix.Symbol("ethbtc")) #55=ethbtc
        trade.setField(fix.Side(fix.Side_BUY)) #54=1 Buy
        trade.setField(fix.OrdType(fix.OrdType_LIMIT)) #40=2 Limit order
        trade.setField(fix.OrderQty(9)) #38=9
        trade.setField(fix.Price(1.5)) #44=1.5
        trade.setField(fix.StringField(60,(datetime.utcnow().strftime ("%Y%m%d-%H:%M:%S.%f"))[:-3]))  #60 TransactTime, not supported in python, so use tag number
        trade.setField(fix.Text("New Order"))  #58 text
        print(trade.toString())
        try:
            fix.Session.sendToTarget(trade, self.sessionID)
        except (fix.ConfigError, fix.RuntimeError) as e:
             print(e)

    @echo
    def cancel_order(self):
        message = fix.Message()
        header = message.getHeader()
        header.setField(fix.BeginString(fix.BeginString_FIX42))
        header.setField(fix.MsgType(fix.MsgType_OrderCancelRequest)) #35=F
        originalOrderId = str(int(self.genOrderID()) - 1)
        message.setField(fix.OrigClOrdID(originalOrderId))
        message.setField(fix.ClOrdID(str(int(originalOrderId) + 1)))  #11=Unique order id
        message.setField(fix.OrderID("375"))
        message.setField(fix.Symbol("ABCD"))   #55=ABCD
        message.setField(fix.Side(fix.Side_BUY))  #54=1 Buy
        message.setField(fix.StringField(60,(datetime.utcnow().strftime ("%Y%m%d-%H:%M:%S.%f"))[:-3]))  #60 TransactTime, not supported in python, so use tag number
        message.setField(fix.Text("Cancel my Order"))  #58 text
        print(message.toString())
        try:
            fix.Session.sendToTarget(message, self.sessionID)
        except (fix.ConfigError, fix.RuntimeError) as e:
            print(e)

    @echo
    def request_order(self):
        message = fix.Message()
        header = message.getHeader()
        header.setField(fix.BeginString(fix.BeginString_FIX42))
        header.setField(fix.MsgType(fix.MsgType_OrderStatusRequest)) #35=H
        message.setField(fix.ClOrdID("*"))
        message.setField(fix.OrderID("375"))
        message.setField(fix.Side(fix.Side_BUY)) #54=1 Buy
        message.setField(fix.Symbol("ABCD"))   #55=ABCD
        print(message.toString())
        try:
            fix.Session.sendToTarget(message, self.sessionID)
        except (fix.ConfigError, fix.RuntimeError) as e:
            print(e)

    @echo
    def list_cancel_request(self):
        message = fix.Message()
        header = message.getHeader()
        header.setField(fix.BeginString(fix.BeginString_FIX42))
        header.setField(fix.MsgType(fix.MsgType_ListCancelRequest)) #35=K
        message.setField(fix.ListID("List123Test"))
        message.setField(fix.StringField(60,(datetime.utcnow().strftime ("%Y%m%d-%H:%M:%S.%f"))[:-3]))  #60 TransactTime, not supported in python, so use tag number
        print(message.toString())
        try:
            fix.Session.sendToTarget(message, self.sessionID)
        except (fix.ConfigError, fix.RuntimeError) as e:
            print(e)
def main(config_file):
    try:
        settings = fix.SessionSettings(config_file)
        application = Application()
        storeFactory = fix.FileStoreFactory(settings)
        logFactory = fix.FileLogFactory(settings)
        initiator = fix.SocketInitiator(application, storeFactory, settings, logFactory)
        initiator.start()

        while 1:
                myInput = input()
                if myInput == "1":
                    print("New Order")
                    application.new_order()
                elif myInput == "2":
                    print("Cancel Order")
                    application.cancel_order()
                elif myInput == "3":
                    print("Get order status")
                    application.request_order()
                elif myInput == "4":
                    print("Test unsupported msg type")
                    application.list_cancel_request()
                elif myInput == "5":
                    sys.exit(0)
                elif myInput == "d":
                    import pdb
                    pdb.set_trace()
                else:
                    print("Valid input is 1 for new order, 2 for cancel order, 3 for order status, 4 for unsupported msg, 5 to exit")
                    continue
    except (fix.ConfigError, fix.RuntimeError) as e:
        print(e)

if __name__=='__main__':
    parser = argparse.ArgumentParser(description='FIX Client')
    parser.add_argument('file_name', type=str, help='Name of configuration file')
    args = parser.parse_args()
    main(args.file_name)