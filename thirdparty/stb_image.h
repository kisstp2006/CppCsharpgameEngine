// stb_image - v2.29 - public domain image loader - http://nothings.org/stb_image.h
// See end of file for full license.

#ifndef STB_IMAGE_H
#define STB_IMAGE_H

// only minimal subset used by this project

extern "C" {

extern unsigned char* stbi_load(char const* filename, int* x, int* y, int* comp, int req_comp);
extern void stbi_image_free(void* retval_from_stbi_load);
extern void stbi_set_flip_vertically_on_load(int flag_true_if_should_flip);

}

#endif // STB_IMAGE_H
