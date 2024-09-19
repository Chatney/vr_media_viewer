## VR media viewer
I wanted to have a VR image viewer to browse old photos and relive them vividly.<br/>

So this is an attempt at making a 3d VR viewer for image and video files that does not require any preprocessing.<br/>
You can manipulate the image to be really IN it, and browse to the next by using your VR controlers.<br/>

Using Unity Sentis and a depth_anything onnx model to create a mesh, and projecting the image frame.<br/>
The mesh uses the full float16 depth information, so the depth resolution has no stair-stepping as opposed to rendering it to a compressed image first.<br/>

## Photos
Image display works pretty well, even for extreme image resolution, so that uses the depth_anything-v2_vit_L model.<br/>
The full resolution image is projected onto the lower resolution mesh, so higher resolution images look way better than video.<br/>
For a 40MP image, on a RTX3090, generating the mesh and material takes about 0.5 seconds, so I guess this might also work on lower end computers.<br/>
![chalet](https://media3.giphy.com/media/v1.Y2lkPTc5MGI3NjExa2p4cjcyNnc4cnR1NmJtYnkybG82aWtmaHl4bXJyaXkwNXN3MW8xcSZlcD12MV9pbnRlcm5hbF9naWZfYnlfaWQmY3Q9Zw/1Rp4q6d7t97r17hNX5/giphy.gif)


## Video
The video playback works but is pretty janky and taxing. So video uses the depth_anything-v2_vid_B model.<br/>
For < 1080p videos the framerate is passable at best, and the pause button is broken (works, but janky). But still very cool.<br/>
For video, the depth values are not normalized in any way, this means that the resulting mesh is pretty stable.<br/>

In my experience making a full build makes the video playback a lot smoother as compared to just pressing play in the editor.<br/>

## Yes
-Single-threaded spaghetti.<br/>
-If you have no image after pressing the pause button, press the video timeline and it should maybe reappear.<br/>
-If you lost the file browser, look up. You most likely scrolled very far and it somehow doesn't reset the scroll position when loading a new folder.<br/>

## Credits

the onnx models were downloaded from https://github.com/fabio-sim/Depth-Anything-ONNX/releases.<br/>

```bibtex
@article{yang2024depth,
      title={Depth Anything V2}, 
      author={Lihe Yang and Bingyi Kang and Zilong Huang and Zhen Zhao and Xiaogang Xu and Jiashi Feng and Hengshuang Zhao},
      year={2024},
      eprint={2406.09414},
      archivePrefix={arXiv},
      primaryClass={id='cs.CV' full_name='Computer Vision and Pattern Recognition' is_active=True alt_name=None in_archive='cs' is_general=False description='Covers image processing, computer vision, pattern recognition, and scene understanding. Roughly includes material in ACM Subject Classes I.2.10, I.4, and I.5.'}
}
```

```bibtex
@misc{oquab2023dinov2,
  title={DINOv2: Learning Robust Visual Features without Supervision},
  author={Oquab, Maxime and Darcet, Timothée and Moutakanni, Theo and Vo, Huy V. and Szafraniec, Marc and Khalidov, Vasil and Fernandez, Pierre and Haziza, Daniel and Massa, Francisco and El-Nouby, Alaaeldin and Howes, Russell and Huang, Po-Yao and Xu, Hu and Sharma, Vasu and Li, Shang-Wen and Galuba, Wojciech and Rabbat, Mike and Assran, Mido and Ballas, Nicolas and Synnaeve, Gabriel and Misra, Ishan and Jegou, Herve and Mairal, Julien and Labatut, Patrick and Joulin, Armand and Bojanowski, Piotr},
  journal={arXiv:2304.07193},
  year={2023}
}
```
