shader_type canvas_item;
// 铁幕突袭 — RA2风格屏幕后处理着色器
// 使用 SCREEN_TEXTURE 采样整个画面，实现全屏后处理

uniform float vignette_intensity : hint_range(0.0, 2.0) = 0.65;
uniform float vignette_radius : hint_range(0.3, 2.0) = 0.65;
uniform float contrast : hint_range(0.5, 2.0) = 1.1;
uniform float saturation : hint_range(0.0, 2.0) = 1.12;
uniform float brightness : hint_range(-0.5, 0.5) = 0.02;
uniform vec3 tint_color : source_color = vec3(1.0, 0.95, 0.85);
uniform float tint_intensity : hint_range(0.0, 1.0) = 0.08;

void fragment() {
    vec4 col = texture(SCREEN_TEXTURE, SCREEN_UV);

    // 1. 亮度微调
    col.rgb += brightness;

    // 2. 对比度（以0.5为中心）
    col.rgb = (col.rgb - 0.5) * contrast + 0.5;

    // 3. 饱和度
    float gray = dot(col.rgb, vec3(0.299, 0.587, 0.114));
    col.rgb = mix(vec3(gray), col.rgb, saturation);

    // 4. 暖色调偏移
    col.rgb = mix(col.rgb, col.rgb * tint_color, tint_intensity);

    // 5. 暗角效果
    vec2 center = vec2(0.5, 0.5);
    float dist = distance(SCREEN_UV, center);
    float vignette = 1.0 - smoothstep(vignette_radius, vignette_radius + 0.5, dist) * vignette_intensity;
    col.rgb *= vignette;

    col.rgb = clamp(col.rgb, 0.0, 1.0);
    COLOR = col;
}
