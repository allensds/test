import websocket, json, hmac, hashlib
try:
    import thread
except ImportError:
    import _thread as thread
import time

def on_message(ws, message):
    print(message)
    msg = json.loads(message)
    if 'challenge' in msg:
        access = 'Dsfzxj7juZTogLSvCERSKVP574Zw762nHJyquLxg'
        secret = 'kLgHtx0jz7sdGtUPxnIQygqZBZM4zABTxpq8VDa7'
        load = access + msg['challenge']
        sig = hmac.new(bytes(secret, 'utf-8'), bytes(load, 'utf-8'), hashlib.sha256).hexdigest()
        ack = { 'auth': {'access_key': access, 'answer': sig}}
        ws.send(json.dumps(ack))

def on_error(ws, error):
    print(error)

def on_close(ws):
    print("### closed ###")

def on_open(ws):
    return

if __name__ == "__main__":
    websocket.enableTrace(True)
    ws = websocket.WebSocketApp("ws://192.168.33.10:8080/",
                              on_message = on_message,
                              on_error = on_error,
                              on_close = on_close)
    ws.on_open = on_open
    ws.run_forever()