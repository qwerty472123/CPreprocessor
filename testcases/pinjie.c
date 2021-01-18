#define aa(x) x##aa##x aa##x##aa
#define aac(x) # x #@ x
aa(123a)
aac(3)
