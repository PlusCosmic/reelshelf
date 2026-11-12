-- Which app a device sign-in is for (ADR-0010): the desktop uploader or the macOS monitor app. Only a monitor's token
-- reaches the admin endpoints the monitor reads, so an Admin's uploader token can't. Every existing request and token
-- is the uploader's, and the default keeps an image that doesn't know the column writing the uploader's.
ALTER TABLE device_authorization
    ADD COLUMN IF NOT EXISTS client text NOT NULL DEFAULT 'uploader'
        CONSTRAINT device_authorization_client_check CHECK (client IN ('uploader', 'monitor'));

ALTER TABLE device_token
    ADD COLUMN IF NOT EXISTS client text NOT NULL DEFAULT 'uploader'
        CONSTRAINT device_token_client_check CHECK (client IN ('uploader', 'monitor'));
