/* Host adapter only. The original packer, image reader and pixel-copy routine
 * are compiled unchanged from explicit, verified local inputs. */
#define main source_unused_command_line
#include "c_hack.c"
#undef main

int main(int argc, char **argv)
{
    int i;
    tga_element *element;
    tga_pixel *image;
    if (argc != 6) return 2;
    pack_width = 128;
    pack_height = 256;
    tga_cache = NULL;
    for (i = 0; i < 4; ++i) {
        element = util_xcalloc(sizeof(tga_element));
        element->file_next = tga_cache;
        element->player_flag = i != 3;
        element->texture_index = i == 3 ? 0 : i + 2;
        tga_read(argv[i + 2], i == 2 || i == 3, &element->header, &element->image);
        tga_cache = element;
    }
    image = pack_textures();
    if (image == NULL) return 3;
    for (element = tga_cache, i = 3; element != NULL; element = element->file_next, --i)
        printf("SOURCE_PACK_PLACEMENT %d %d %d %d %d\n", i, element->new_left,
            element->new_top, element->header.width, element->header.height);
    tga_write(argv[1], pack_width, pack_height, TRUE, image);
    tga_free(image);
    while (tga_cache != NULL) {
        element = tga_cache;
        tga_cache = element->file_next;
        tga_free(element->image);
        free(element);
    }
    return 0;
}
