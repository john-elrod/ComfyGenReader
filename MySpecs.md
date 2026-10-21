I want to create a lightweight windows desktop application that will gather stable diffusion meta data in ComfUI AI generated pictures and videos.

This tool will allow users to browse to a file, or drag and drop it in. It will show the picture or video, and give a text box that shows the JSON output in a pretty format. It will convert /n and other codes to the proper output.

The UI will have 3 frames. A top frame that extends the width of the window. A left frame under the top frame that extends 60% of the width of the window, and a right frame that extends 40%. The window should have a minimum width of 400 pixels, and no maximum. The height minimum should be 400 pixels, and no max.

The top frame will have a path that can be directly edited, with a browse button next to it and a GO button. 

Under the path bar, in the same frame, it will have dropdown of recent 10 files opened. This should be a 100% width.

The left frame will contain the preview image/video when one is open. Files can also be dragged into that frame to load it. When no file is open, a "drag here" with icon will appear. When files are dragged in, the path should be reflect the update. On the bottom of the left frame will be a left and right button, which will navigate to the next appropriate file in the folder. When navigating using the forward and back, these files should not appear in the historical drop down - just files that were dragged in, or entered manually in the path, or browsed too. 

The right frame will contain 2 sections. The top contains settings to highlight found within the meta data (json). Seed and prompt. The prompt should be multiline (5 lines) with a scroll bar.

The second section of the right frame will contain a raw dump of the JSON, but in pretty format.

Below the second section, have a small button "export" that will export the json from the meta data to a valid json file for download. In theory, this export will be able to be imported into comfyui as a workflow.

When errors occur, populate the raw json with the exception. Do not allow the app to crash entirely - use a wrapping try/catch. Export the crash exception first, then a stack output.

File types to handle
- Traditional JPEG extensions (jpg, jpeg, ect)
- web pictures and web movies (webm, webp)
- mp4, avi

Other files types should be ignored for now.

When a picture loads, it should be zoomed to fill the available space. It should not distort the image - zoom to an appropriate edge.

When movies are loaded, show controls to play, stop, and a time tracking bar that shows a preview of the movie as you scrub through it. It will have a mute/unmute button that is muted by default. It should also have an appropriate zoom to fill the space like photos.

Put all these requirements into a new Specs.md file. Be straight forward and minimalist with the documentation.

Build this application using some for of .net 10. I want this to be a portable application - One i can zip up and copy to other computers to execute. I am not worried about self signing.

If using third party libraries, confirm they do not have any critical vulerabilities.

For storing historical data, and potential future configurations, use json files that are stored with the application files.

If third party dlls and other files are needed to keep this portable, keep them in a folder in the root of the application if possible.



