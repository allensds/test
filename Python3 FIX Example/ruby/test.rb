class TestClass
    def test_function(some_var)
        if some_var == 'test'
            puts "This may take some time"
            # something is done here with some_var
            puts "Finished"
        else
            # just do something short with some_var
            puts "Do nothing"
        end
        return some_var
    end
end