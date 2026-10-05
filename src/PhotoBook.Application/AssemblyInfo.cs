using System.Runtime.CompilerServices;

// The adapters in this assembly exist to be constructed once and injected as their interface
// everywhere else. Making them internal is what turns that from a convention into something
// the compiler enforces: a view, a dialog or a converter that reaches for a concrete
// implementation of a port does not build.
//
// Only the tests may construct them. The composition root asks each layer to assemble itself
// (see ApplicationServices), so nothing in the program itself names a concrete
// implementation - and that is what the compiler checks, instead of a reviewer.
[assembly: InternalsVisibleTo("PhotoBook.Application.Tests")]
