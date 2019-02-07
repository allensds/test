import hmac, binascii, base64,time, urllib.request, urllib.parse, requests, hashlib, datetime
import simplejson as json

tests = json.loads(open("api/api2.json").read())
settings = tests['Settings']
cases = tests['Cases']
results = dict()

def getTonce():
    return str(int(time.time() * 1000))

def callAPI(case):
    if case['Type'] == "Public":
        #Construct Payload = "#{canonical_verb}|#{canonical_uri}|#{canonical_query}"
        query = ''
        if 'Query' in case and case['Query']:
            for key, value in case['Query'].items():
                query += '&' + key + '=' + value
        load = '|'.join([case['Verb'], '/api' + case['Method'],query])
        print('load=' + load)
        
        #Construct URL
        url = settings['URL'] + '/api' + case['Method'] + '?' + query
        print('url = ' + url)
        response = requests.get(url)
        print(response.text)
        return response.text

    elif case['Type'] == "Private":
        #Construct Payload = "#{canonical_verb}|#{canonical_uri}|#{canonical_query}"
        query = 'access_key=' + settings['AccessKey']
        if 'Query' in case and case['Query']:
            mydict = case['Query']
            mydict['tonce'] = getTonce()
            keylist = list(mydict.keys())
            keylist.sort()

            for key in keylist:
                query += '&' + key + '=' + str(mydict[key])
        else:
            query += '&tonce=' + getTonce()

        load = '|'.join([case['Verb'], '/api' + case['Method'],query])
        print('load=' + load)
        sig = hmac.new(bytes(settings['SecretKey'], 'utf-8'),
                       bytes(load, 'utf-8'),
                       hashlib.sha256).hexdigest()
        
        #Construct URL
        url = settings['URL'] + '/api' + case['Method'] + '?' + '&'.join([query, 'signature=' + str(sig)])
        print('url = ' + url)
        if case['Verb'] == 'GET':
            response = requests.get(url)
        else:
            response = requests.post(url, case['Query'])
        print(response.text)
        return response.text
    else:
        print("Unknow type.")
        return

def authenticateMe(accessKey, secretKey):
    query = 'access_key=' + accessKey
    query += '&tonce=' + getTonce()

    load = '|'.join(['GET', '/api' + '/v2/members/me', query])
    print('load=' + load)
    sig = hmac.new(bytes(secretKey, 'utf-8'),
                    bytes(load, 'utf-8'),
                    hashlib.sha256).hexdigest()
        
    url = 'http://192.168.33.10:3000' + '/api' + '/v2/members/me' + '?' + '&'.join([query, 'signature=' + str(sig)])
    print('url = ' + url)
    response = requests.get(url)
    print(response.text)
    return response.text

def createOrder(accessKey, secretKey, data):
    query = 'access_key=' + accessKey

    data['tonce'] = getTonce()
    keylist = list(data.keys())
    keylist.sort()

    for key in keylist:
        query += '&' + key + '=' + str(data[key])
          
    load = '|'.join(['POST', '/api' + '/v2/orders', query])
    print('load=' + load)
    sig = hmac.new(bytes(secretKey, 'utf-8'),
                    bytes(load, 'utf-8'),
                    hashlib.sha256).hexdigest()
        
    url = 'http://192.168.33.10:3000' + '/api' + '/v2/orders' + '?' + '&'.join([query, 'signature=' + str(sig)])
    print('url = ' + url)
    response = requests.post(url, data)
    print(response.text)
    return response.text

def getOrder(accessKey, secretKey, orderId):
    query = 'access_key=' + accessKey
    query += '&id=' + str(orderId)
    query += '&tonce=' + getTonce()

    load = '|'.join(['GET', '/api' + '/v2/order', query])
    print('load=' + load)
    sig = hmac.new(bytes(secretKey, 'utf-8'),
                    bytes(load, 'utf-8'),
                    hashlib.sha256).hexdigest()
        
    url = 'http://192.168.33.10:3000' + '/api' + '/v2/order' + '?' + '&'.join([query, 'signature=' + str(sig)])
    print('url = ' + url)
    response = requests.get(url)
    print(response.text)
    return response.text

def cancelOrder(accessKey, secretKey, orderId):
    query = 'access_key=' + accessKey
    query += '&id=' + str(orderId)
    query += '&tonce=' + getTonce()

    load = '|'.join(['POST', '/api' + '/v2/order/delete', query])
    print('load=' + load)
    sig = hmac.new(bytes(secretKey, 'utf-8'),
                    bytes(load, 'utf-8'),
                    hashlib.sha256).hexdigest()
        
    url = 'http://192.168.33.10:3000' + '/api' + '/v2/order/delete' + '?' + '&'.join([query, 'signature=' + str(sig)])
    print('url = ' + url)
    data = {}
    data['id'] = str(orderId)
    response = requests.post(url,data)
    print(response.text)
    return response.text

""" for index, case in enumerate(cases):
    results['Case ' + str(index+1)] = {'Input': case}
    out = callAPI(case)
    results['Case ' + str(index+1)]['Output'] = json.loads(out)

report = open((datetime.datetime.now().strftime('API_TestResults_%Y%m%d%H%M%S')) + '.json', 'a')
report.write(json.dumps(results, indent = 4))
report.close() """