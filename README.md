Trakt WMC Scheduler
===================
This is a simple application that watches your Trakt.tv Watchlist for movies, and when they show up in the Windows Media Center EPG, schedules recordings.

Quick Start
===========
To get things set up, launch the application and click the _Trakt Login_ button. Once you allow Trakt WMC Scheduler to access your Trakt account, it will periodically grab your Watchlist and check for matching movies. When the recording completes, it will be added to your Trakt collection.

Event Logging
=============
Trakt WMC Scheduler will log events in the Media Center event log when it schedules a recording or a recording completes. To use this feature, Trakt WMC Scheduler needs to be run once with Administrator permissions so it can register itself as an event source. After that, Trakt WMC Scheduler can log events even when run normally.

Known Issues
============
Trakt WMC Scheduler was _good enough_ for a first pass, but there are a few issues:
* Recordings are only scheduled if the program is in HD
* Adding items to your collection is an option, but it's only configurable by editing the user settings file (or the TraktWmcScheduler.config file)
* There isn't a way to cancel a recording from Trakt WMC Scheduler (you can cancel them from Media Center, though)
* There isn't a (good) way to reschedule a recording
* Trakt WMC Scheduler will record anything on your watchlist, even if you alread have it in your collection.
I'm sure there are others, but those are the obvious ones. I hope to address those some day.

Building
========
Trakt WMC Scheduler builds under Visual Studio 2013. It targets the .NET Framework v4.5, and you need Windows Media Center installed (or a copy of its libraries) to get it to build properly.

If you want the Entity Framework designer to work, you need the SQLite provider from http://system.data.sqlite.org/index.html/doc/trunk/www/downloads.wiki. ErikEJ has a good set of instructions here: http://erikej.blogspot.com/2014/11/using-sqlite-with-entity-framework-6.html. Visual Studio is perfectly happy to build without that, though. Note that you probably want the x86 provider package even if you have an x64 system - at least, I did.

Contributing
============
If there's something that's not working right, or you would like to see added, please file an issue. I'll look at them eventually. If you want to fix things, pull requests are welcome!
If you're going to fork this project (and I mean actually fork, not just creating a copy for contributing), please be considerate and request your own Trakt API key.
