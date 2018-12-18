Python3 FIX
======================

Python3 quickfix executor and client applications.

*Requirements: 
Python3 and QuickFix. 
For Linux environment, make sure you have python3 installed and run below command to install quickfix:
pip3 install quickfix
For Windows environment, make sure you have python3 installed and download the quickfix python wheel for the correct python version from:
https://www.lfd.uci.edu/~gohlke/pythonlibs/#quickfix and run below command to install it:
pip install [Wheel file path downloaded from above link]

For QuickFix Documentation, refer to http://www.quickfixengine.org/quickfix/doc/html/?quickfix/doc/html

*Configs:
The configuration files for executor and client are executor.cfg and client.cfg respectively, and default to use FIX 4.4
If you want the executor to support multiple versions, comment out the session for other versions. T

*Run both server and client:
python3 executor.py executor.cfg (Executes, cancels limit orders or requests order status)
python3 client.py client.cfg (Interactive order placement, cancellation or query)

1 - place new order
2 - cancel order
3 - request order status
4 - exit
t - test unsupported message type by sending a ListCancelRequest
d - pdb.set_trace() - to check what a mess inside a SWIG generated code

Check what is going on in Logs folder

To enable debug echo, set ECHO_DEBUG = True in executor.py and client.py
