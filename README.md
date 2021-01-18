# CPreprecessor

This is a part of course project in SEU. **THIS IS A TOY PROJECT ONLY.**

This preprecessor support most of all standard directives and internal macros mentioned in [MSDN](https://docs.microsoft.com/zh-cn/cpp/preprocessor/c-cpp-preprocessor-reference?view=msvc-160).

Before all operations, it will perform *Line splicing*("All lines ending in a backslash (**\**) immediately followed by a newline character are joined with the next line in the source file, forming logical lines from the physical lines. Unless it's empty, a source file must end in a newline character that's not preceded by a backslash."), and remove all comment without change lineno.

## Support directives

### #ifdef / ifndef [MACRO_ID\]

There are equal to `#if defined(<MACRO_ID>)` and `#if !defined(<MACRO_ID>)`.

### #if \[COND_EXPR] / #elif [COND_EXPR] / #else / #endif

* Support calculating most of all interger constants expression in case of `sizeof(size_t)  == 8`.
* Support auto-conversation from `long long` to `unsigned long long`.
* Support `defined(<MACRO_ID>)` and `defined MACRO_ID`.
* Support unsigned integer tailing `u` or `sz`(C++23), other tailing letter characters will be ignored(All integer is `long long` or `unsigned long long`) and integers representing by a char(`'[char]'`) or starting by `0x`, `0b` or simple leading zeros will be recognized correctly.

### #error \<ERROR\>

It will raise an error unless is under unsatisified condition directive.

### #include \<[file_name]\> | \"[file_name]\" | [MACRO_IDs\]

Include one file.

Search path includes specfied by `-i` parameter and the envirionment variable `INCLUDE`, which should obey the form in your platform.

### #include_once \<[file_name]\> | \"[file_name]\" | [MACRO_IDs\]

Include one file unless it had been included(even by `#include`).

### #pragma once

If this directive is encountered in the same file for the second time, the output of the contents of the current file will be terminated immediately. (It won't check if it is in the head of the whole file).

### #define [MACRO_ID\]([args]) [RULES] | [MACRO_ID\] [RULES]

* Support the detection of the recursion defined.
* Support `__VA_ARGS__`.
* Support stringizing operator(`#`), charizing operator(`#@`) and token-pasting operator(`##`).

### #undef [MACRO_ID\]

Clear the defined macro.

## Support predefined macros

* `__STDC_NO_ATOMICS__` always be `1`.
* `__STDC_NO_COMPLEX__` always be `1`.
* `__STDC_NO_THREADS__` always be `1`.
* `__STDC_NO_VLA__` always be `1`.
* `__STDC_VERSION__` always be `199901L`.
* `__DATE__` The date format by `MMM dd yyyy` in `en-US` culture.(In .NET; It's equal to the format by `Mmm dd yyyy`),
* `__TIME__` The date format by `hh:mm:ss`.
* `__FILE__` the name of current file.(It will covered user-define while switching file.)
* `__LINE__` the lineno of current file.

## Usage

```plain
CPreprocessor [options...] [c files...]
Options:
-h | --help | /?        show this text
-m | --multi-thread     use multi thread to assemble
-s | --show-level       define the output show level (verbose|detail|infomation|warning|error|critical)
-f | --overwrite        force overwrite file
-o | --output-directoy <directory>      output directory
-of | --output-file <filename>  output file name
-i | --include-path <directory> add an include path
```

## Passthrough

* *String concatenation* should be performed after CPreprecessor.(for giving correct lineno to the following parser)
* `# <lineno> "<filename>"` will be added for parser in following step to resolve lineno.
* All `#line` and `#pragma`(without `#pragma once`) will be pass through.
* CPreprecessor won't parse functions, so the predefined identifier(this is not a macro) `__func__`  won't be replaced by the correct function name.

