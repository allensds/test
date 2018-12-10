import sys
import time
import _thread
import argparse
import quickfix as fix
from tools.echo import echo

class Application(fix.Application):
    orderID = 0
    execID = 0

    @echo
    def onCreate(self, sessionID): return
    @echo
    def onLogon(self, sessionID): return
    @echo
    def onLogout(self, sessionID): return
    @echo
    def toAdmin(self, sessionID, message): 
        print("Sending the following admin message: %s" % message.toString())
        return
    @echo
    def fromAdmin(self, sessionID, message): 
        print("Received the following admin message: %s" % message.toString())
        return
    @echo
    def toApp(self, sessionID, message): 
        print("Sending the following message: %s" % message.toString())
        return
    @echo
    def fromApp(self, message, sessionID):
        print("Received the following message: %s" % message.toString())
        beginString = fix.BeginString()
        msgType = fix.MsgType()
        message.getHeader().getField( beginString )
        message.getHeader().getField( msgType )

        symbol = fix.Symbol()
        side = fix.Side()
        ordType = fix.OrdType()
        orderQty = fix.OrderQty()
        price = fix.Price()
        clOrdID = fix.ClOrdID()

        message.getField( ordType )
        print(ordType)
        if ordType.getValue() != fix.OrdType_LIMIT:
            raise fix.IncorrectTagValue( ordType.getField() )

        message.getField( symbol )
        message.getField( side )
        message.getField( orderQty )
        message.getField( price )
        message.getField( clOrdID )

        executionReport = fix.Message()
        executionReport.getHeader().setField( beginString )
        executionReport.getHeader().setField( fix.MsgType(fix.MsgType_ExecutionReport) )

        executionReport.setField( fix.OrderID(self.genOrderID()) )
        executionReport.setField( fix.ExecID(self.genExecID()) )
        executionReport.setField( fix.OrdStatus(fix.OrdStatus_FILLED) )
        executionReport.setField( symbol )
        executionReport.setField( side )
        executionReport.setField( fix.CumQty(orderQty.getValue()) )
        executionReport.setField( fix.AvgPx(price.getValue()) )
        executionReport.setField( fix.LastShares(orderQty.getValue()) )
        executionReport.setField( fix.LastPx(price.getValue()) )
        executionReport.setField( clOrdID )
        executionReport.setField( orderQty )

        if beginString.getValue() == fix.BeginString_FIX40 or beginString.getValue() == fix.BeginString_FIX41 or beginString.getValue() == fix.BeginString_FIX42:
            executionReport.setField( fix.ExecTransType(fix.ExecTransType_NEW) )

        if beginString.getValue() >= fix.BeginString_FIX41:
            executionReport.setField( fix.ExecType(fix.ExecType_FILL) )
            executionReport.setField( fix.LeavesQty(0) )

        try:
            fix.Session.sendToTarget( executionReport, sessionID )
        except fix.SessionNotFound as e:
            return

    def genOrderID(self):
        self.orderID = self.orderID+1
        return str(self.orderID)
    def genExecID(self):
        self.execID = self.execID+1
        return str(self.execID)

def main(file_name):

    try:
        # "C:\\allen\\quickfix-python-sample-master\\executor.cfg"
        settings = fix.SessionSettings(file_name)
        application = Application()
        storeFactory = fix.FileStoreFactory( settings )
        logFactory = fix.FileLogFactory( settings )
        print('creating acceptor')
        acceptor = fix.SocketAcceptor( application, storeFactory, settings, logFactory )
        print('starting acceptor')
        acceptor.start()

        while 1:
            time.sleep(1)
    except (fix.ConfigError, fix.RuntimeError) as e:
        print(e)

if __name__=='__main__':
    #parser = argparse.ArgumentParser(description='FIX Server')
    #parser.add_argument('file_name', type=str, help='Name of configuration file')
    #args = parser.parse_args()
    #main(args.file_name)
    main("C:\\allen\\quickfix-python-sample-master\\executor.cfg")
