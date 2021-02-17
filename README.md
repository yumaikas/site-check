# site-check

Based on angle-sharp, this is a very basic tool for checking if your sites are up and running. 
It's designed to be a lightweight drop in for poking sites with selenium.

## Installation

Build/Publish using VS-2019, and then copy the files where you'd like them. Releases will be added at a later date.

## Example/usage

For example, assuming that site-check.exe is on your path, and with powershell.

```powershell
site-check ` 
-site https://junglecoder.com --sel body --has-text "musings of a" --out blog-up ` 
-site https://idea.junglecoder.com --sel body --has-text "Sort by:" --out wiki-up `
-site does-not-exist --out should-be-false
```

This should ouput the following:

```
{"blog-up":true,"wiki-up":true,"should-be-false":null}
```

(Long term, should-be-false will be switched to false, and fault messages will be added.)



