shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;
uniform float scanline_strength : hint_range(0.0, 1.0) = 0.2;
uniform float vignette_strength : hint_range(0.0, 1.0) = 0.45;

void fragment() {
    // Sample the scene behind the ColorRect
    vec4 scene_color = texture(screen_texture, SCREEN_UV);
    
    // Calculate scanlines (darkens every 3rd row)
    float line = step(1.0, mod(FRAGCOORD.y, 3.0));
    float scanline_factor = mix(1.0, line, scanline_strength);
    
    // Calculate vignette (darkens edges)
    float vig = smoothstep(0.3, 0.75, length(UV - vec2(0.5)));
    float vignette_factor = 1.0 - (vig * vignette_strength);
    
    // Apply darkening effects to the RGB channels
    scene_color.rgb *= scanline_factor * vignette_factor;
    
    COLOR = scene_color;
}
