#[compute]
#version 450
// Copies the scene colour into a texture the stylize pass can sample around (it then writes the scene colour).
layout( local_size_x = 8, local_size_y = 8, local_size_z = 1 ) in;
layout( rgba16f, set = 0, binding = 0 ) uniform restrict readonly image2D src;
layout( rgba16f, set = 0, binding = 1 ) uniform restrict writeonly image2D dst;
void main()
{
	ivec2 gid = ivec2( gl_GlobalInvocationID.xy );
	ivec2 size = imageSize( src );
	if ( gid.x >= size.x || gid.y >= size.y ) return;
	imageStore( dst, gid, imageLoad( src, gid ) );
}
