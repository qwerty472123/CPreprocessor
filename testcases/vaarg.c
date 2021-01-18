#define a(a, ...) __VA_ARGS__

#define b(...) __VA_ARGS__

a(1,2,3);
b(1,2,3);
//a(); //require at least 2 arguments, but 0 provided