//=== https://git-lfs.com/

git-lfs : Git Large File Storage
An open source Git extension for versioning large files

Download and install the Git command line extension - "git-lfs". 
[https://github.com/git-lfs/git-lfs/releases/download/v3.6.0/git-lfs-linux-amd64-v3.6.0.tar.gz]
Once downloaded and installed, 

**set up** Git LFS for your user account by running:
`git lfs install`
You only need to run this **once** per user account.

In each Git repository where you want to use Git LFS, select the file types you'd like Git LFS to manage 
(or directly edit your .gitattributes). You can configure additional file extensions at anytime.
[ex]
`git lfs track "*.psd"`

Now make sure .gitattributes is tracked:
`git add .gitattributes`

Note that defining the file types Git LFS should track will not, by itself, 
convert any pre-existing files to Git LFS, such as files on other 


//=== to get the 'actual' simsum.ttf under SkiaTextRenderer.Test/Fonts/
$ cd /mnt/sdb1/cspdf/SkiaTextRenderer/
run  `git lfs install` if not done yet

chk simsun.ttf file size before git-lfs pull
$ ls -altr SkiaTextRenderer.Test/Fonts/

$ git lfs pull

chk simsun.ttf file size after git-lfs pull,
it will be much larger than the initial size.
$ ls -altr SkiaTextRenderer.Test/Fonts/
