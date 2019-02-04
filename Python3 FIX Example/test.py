#from subprocess import call
#answer = call(["ruby", "-r", "./ruby/test.rb", "-e", "puts TestClass.test_function('some meaningful text')"])

from subprocess import check_output
answer = check_output(["ruby", "-r", "./ruby/test.rb", "-e", "puts TestClass.test_function('test')"])

print(answer)