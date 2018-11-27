## KMB_ImageComparison: PHASH and SIFT comparing local images to a set of images from Picturepark

The images can have slightly different angles or cutouts and will still be matched.


### Resources 
* Implementation of PHASH <https://github.com/pgrho/phash>
* Implementation of SIFT from emgu CV <https://github.com/emgucv>

#### Summary of the guidelines:

* Tool supports use of proxy, configured in the ..exe.config file (leave address empty to not use proxy)
* Both configuration files [..exe.config and Configuration.txt] have to be in the same folder as .exe
* Use the "generate configuration file"-option to create an configuration file with default values
* You can find the precompiled program in the BUILD folder 