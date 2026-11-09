// GPU sand puffs for the crater's sand cloud (s&box SandCloud behaviour). Emitted from code with a velocity (m/s),
// a colour, and CUSTOM = ( size in inches, life in seconds, 0, 0 ): drag, a little buoyancy, grow and fade out.
shader_type particles;

const float IN = 0.0254;

void start()
{
	if ( !RESTART_CUSTOM ) { ACTIVE = false; }
	else { CUSTOM.w = 0.0; }
}

void process()
{
	CUSTOM.w += DELTA;
	float k = clamp( CUSTOM.w / max( CUSTOM.y, 0.01 ), 0.0, 1.0 );
	if ( CUSTOM.w >= CUSTOM.y ) { ACTIVE = false; }
	VELOCITY *= exp( -1.6 * DELTA );
	VELOCITY += vec3( 0.0, 22.0 * IN * DELTA, 0.0 );
	float grow = 1.0 + k * 0.5;
	float fade = k < 0.7 ? 1.0 : 1.0 - ( k - 0.7 ) / 0.3;
	float s = max( CUSTOM.x * grow * clamp( fade, 0.0, 1.0 ) * 2.0 * IN, 0.00001 );
	TRANSFORM[0].xyz = vec3( s, 0.0, 0.0 );
	TRANSFORM[1].xyz = vec3( 0.0, s, 0.0 );
	TRANSFORM[2].xyz = vec3( 0.0, 0.0, s );
}
