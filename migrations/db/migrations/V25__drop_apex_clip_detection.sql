-- Remove the OpenCV-based Apex legend detection. The API code that used this table has been deleted;
-- legend detection is being rebuilt from scratch.
DROP TABLE IF EXISTS apex_clip_detection;
